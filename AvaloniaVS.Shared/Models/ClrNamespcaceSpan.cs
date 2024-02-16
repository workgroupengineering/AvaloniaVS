internal record ClrNamespaceSpan(string Source, int Start, int End )
{
    public override string ToString()
    {
        return Source[Start..End];
    }
}
