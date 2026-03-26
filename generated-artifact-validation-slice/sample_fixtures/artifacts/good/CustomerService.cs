using Demo.Generated.Contracts;

namespace Demo.Generated.Services;

public sealed class CustomerService : ICustomerService
{
    public string DescribeCustomer(int id)
    {
        return $"customer-{id}";
    }
}

