namespace JsonToCuplLib
{
    public interface IPins
    {
        int this[string pinName] { get; }
    }
}