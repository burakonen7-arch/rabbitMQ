#!/usr/bin/env python3
"""Çalışan iki API ve yerel RabbitMQ üzerinde işlevsel test. Üçüncü taraf paket gerektirmez."""
import base64
import json
import os
import time
import urllib.error
import urllib.request
import uuid

ORDERS = os.getenv('ORDERS_URL', 'http://127.0.0.1:5001')
NOTIFICATIONS = os.getenv('NOTIFICATIONS_URL', 'http://127.0.0.1:5002')
BROKER = os.getenv('RABBITMQ_MANAGEMENT_URL', 'http://127.0.0.1:15672')

def request(url, body=None, broker=False):
    headers = {'Content-Type': 'application/json'}
    if broker:
        credentials = os.getenv('RABBITMQ_USER', 'guest') + ':' + os.getenv('RABBITMQ_PASSWORD', 'guest')
        headers['Authorization'] = 'Basic ' + base64.b64encode(credentials.encode()).decode()
    req = urllib.request.Request(url, data=None if body is None else json.dumps(body).encode(), headers=headers)
    try:
        with urllib.request.urlopen(req, timeout=10) as response:
            return response.status, json.load(response)
    except urllib.error.HTTPError as error:
        return error.code, json.load(error)

def eventually(check, description):
    deadline = time.monotonic() + 20
    while time.monotonic() < deadline:
        result = check()
        if result:
            return result
        time.sleep(0.2)
    raise AssertionError('Zaman aşımı: ' + description)

def notifications():
    status, body = request(NOTIFICATIONS + '/api/notifications')
    assert status == 200, body
    return body

def publish(payload):
    status, body = request(BROKER + '/api/exchanges/%2F/orders.events/publish', {
        'properties': {'delivery_mode': 2, 'content_type': 'application/json'},
        'routing_key': 'order.created', 'payload': payload, 'payload_encoding': 'string'
    }, broker=True)
    assert status == 200 and body['routed'], body

# 1. İstek doğrulaması
for body in [{'product': '', 'quantity': 1}, {'product': '   ', 'quantity': 1},
             {'product': 'Kalem', 'quantity': 0}, {'product': 'Kalem', 'quantity': 1001}]:
    status, _ = request(ORDERS + '/api/orders', body)
    assert status == 400, (body, status)
print('PASS: Geçersiz istekler HTTP 400')

# 2. Controller -> publisher -> exchange -> queue -> consumer -> inbox
status, order = request(ORDERS + '/api/orders', {'product': 'Test-' + uuid.uuid4().hex[:8], 'quantity': 3})
assert status == 202, (status, order)
received = eventually(lambda: next((x for x in notifications() if x['orderId'] == order['id']), None), 'sipariş teslimi')
assert received['product'] == order['product'] and received['quantity'] == 3
print('PASS: HTTP 202 ve gerçek RabbitMQ üzerinden mesaj teslimi')

# 3. Aynı EventId tekrar işlense de inbox tek kayıt tutar.
publish(json.dumps({'EventId': received['eventId'], 'OrderId': order['id'],
                    'Product': 'Tekrar teslim', 'Quantity': 99, 'CreatedAt': order['createdAt']}))
# Aynı kanaldaki sırayı dolaylı doğrulamak için ardından ayrı bir işaret mesajı gönder.
marker = str(uuid.uuid4())
publish(json.dumps({'EventId': marker, 'OrderId': str(uuid.uuid4()), 'Product': 'İşaret',
                    'Quantity': 1, 'CreatedAt': order['createdAt']}))
eventually(lambda: any(x['eventId'] == marker for x in notifications()), 'tekrar teslim sonrası işaret')
matching = [x for x in notifications() if x['eventId'] == received['eventId']]
assert len(matching) == 1 and matching[0]['quantity'] == 3, matching
print('PASS: EventId ile tekrar teslimde tek kayıt')

# 4. Bozuk JSON ACK edilmez, requeue=false ile dead-letter kuyruğuna gider.
poison = 'invalid-json-' + uuid.uuid4().hex
publish(poison)
def dead_letter_arrived():
    status, messages = request(BROKER + '/api/queues/%2F/notifications.failed/get',
        {'count': 1000, 'ackmode': 'ack_requeue_true', 'encoding': 'auto'}, broker=True)
    assert status == 200, messages
    return any(m['payload'] == poison and
               m['properties'].get('headers', {}).get('x-first-death-reason') == 'rejected' for m in messages)
eventually(dead_letter_arrived, 'dead-letter teslimi')
print('PASS: Bozuk JSON notifications.failed kuyruğunda (rejected)')
print('4/4 senaryo geçti. Test bildirimleri ve dead-letter mesajı inceleme için bırakıldı.')
