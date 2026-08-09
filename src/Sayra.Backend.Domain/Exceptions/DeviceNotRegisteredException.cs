namespace Sayra.Backend.Domain.Exceptions
{
    public class DeviceNotRegisteredException : DomainException
    {
        public DeviceNotRegisteredException(string message = "The requesting device is not registered.")
            : base("DEVICE_NOT_REGISTERED", message)
        {
        }
    }
}
