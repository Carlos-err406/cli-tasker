namespace cli_tasker;

record TaskStats
{
    public int Total { get; init; }
    public int Checked { get; init; }
    public int Unchecked { get; init; }
    public int Trash { get; init; }
}
