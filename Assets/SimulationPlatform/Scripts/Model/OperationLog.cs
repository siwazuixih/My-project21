using System;

public class OperationLog
{
    public long Timestamp { get; set; }
    public string AccountName { get; set; }
    public string UserName { get; set; }
    public string Operation { get; set; }
    public string Detail { get; set; }
    public string IpAddress { get; set; }
    public DateTime CreateTime { get; set; }
}