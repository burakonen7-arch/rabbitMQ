namespace Orders.Domain;

public sealed record Order(Guid Id, string Product, int Quantity, DateTimeOffset CreatedAt)
{
    public static Order Create(string product, int quantity)
    {
        if (string.IsNullOrWhiteSpace(product) || product.Length > 100)
            throw new ArgumentException("Ürün adı 1-100 karakter olmalıdır.");
        if (quantity is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(quantity));
        return new Order(Guid.NewGuid(), product.Trim(), quantity, DateTimeOffset.UtcNow);
    }
}
