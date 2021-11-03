namespace JsonToCupl
{
    interface IPins
    {
        int this[string pinName] { get; }
    }
}