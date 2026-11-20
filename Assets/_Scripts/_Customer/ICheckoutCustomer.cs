/// <summary>What the customer AI implements so the Register can talk to it.</summary>
public interface ICheckoutCustomer
{
    /// <summary>Cash handed over for this total (coins). Less than the total is raised to the total.</summary>
    int ChooseCash(int total);

    /// <summary>Called once when the checkout ends. completed = false if it was cancelled early.</summary>
    void OnCheckoutEnded(bool completed);
}

/// <summary>Optional contract for customers that require an exact set of scanned products.</summary>
public interface IOrderedCheckoutCustomer : ICheckoutCustomer
{
    bool HasOrder { get; }

    /// <summary>True when the products currently on the counter exactly match this customer's order.</summary>
    bool MatchesOrder(System.Collections.Generic.IReadOnlyList<ProductCarriable> scannedProducts);
}
