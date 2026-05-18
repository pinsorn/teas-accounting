namespace Accounting.Application.Master;

public interface ICustomerService
{
    Task<long> CreateAsync(CreateCustomerRequest req, CancellationToken ct);
    Task UpdateAsync(long customerId, UpdateCustomerRequest req, CancellationToken ct);
    Task<CustomerDto?> GetAsync(long customerId, CancellationToken ct);
    Task<IReadOnlyList<CustomerDto>> ListAsync(string? search, int page, int pageSize, CancellationToken ct);
}
