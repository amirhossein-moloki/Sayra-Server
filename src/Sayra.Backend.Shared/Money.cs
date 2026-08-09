using System;

namespace Sayra.Backend.Shared
{
    public record Money
    {
        public decimal Amount { get; }
        public string Currency { get; }

        public Money(decimal amount, string currency = "SAY")
        {
            Amount = Math.Round(amount, 4, MidpointRounding.AwayFromZero); // Keep standard consumer-intuitive precision
            Currency = currency ?? "SAY";
        }

        public static Money Zero(string currency = "SAY") => new(0, currency);

        public static Money operator +(Money left, Money right)
        {
            ValidateCurrency(left, right);
            return new Money(left.Amount + right.Amount, left.Currency);
        }

        public static Money operator -(Money left, Money right)
        {
            ValidateCurrency(left, right);
            return new Money(left.Amount - right.Amount, left.Currency);
        }

        public static Money operator *(Money left, decimal multiplier)
        {
            return new Money(left.Amount * multiplier, left.Currency);
        }

        private static void ValidateCurrency(Money left, Money right)
        {
            if (left.Currency != right.Currency)
            {
                throw new InvalidOperationException($"Cannot perform arithmetic on different currencies: {left.Currency} and {right.Currency}");
            }
        }

        public override string ToString() => $"{Amount} {Currency}";
    }
}
