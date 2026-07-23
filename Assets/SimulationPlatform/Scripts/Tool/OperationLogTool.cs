using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class OperationLogTool
{
    private static readonly object _lock = new object();
    private static string _dbPath;

    static OperationLogTool()
    {
        string dataDir = Path.Combine(PathTool.GetExecutableDirPath(), "Data", "Operations");
        if (!Directory.Exists(dataDir))
        {
            Directory.CreateDirectory(dataDir);
        }
        _dbPath = Path.Combine(dataDir, "OperationLog.db");
        Debug.Log($"操作日志数据库路径：{_dbPath}");
    }

    public static void RecordLog(OperationType operationType, string detail = "", string ipAddress = "")
    {
        lock (_lock)
        {
            try
            {
                using (var db = new LiteDB.LiteDatabase(_dbPath))
                {
                    var collection = db.GetCollection<OperationLog>("OperationLogs");
                    
                    if (collection.Count() == 0)
                    {
                        collection.EnsureIndex(x => x.Timestamp);
                        collection.EnsureIndex(x => x.AccountName);
                        collection.EnsureIndex(x => x.CreateTime);
                    }

                    var log = new OperationLog
                    {
                        Timestamp = DateTimeOffset.Now.ToUnixTimeMilliseconds(),
                        AccountName = AccountManager.GetCurrentAccountName(),
                        UserName = AccountManager.GetCurrentUserName(),
                        Operation = operationType.ToString(),
                        Detail = detail,
                        IpAddress = ipAddress,
                        CreateTime = DateTime.Now
                    };

                    collection.Insert(log);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"操作日志记录失败：{ex.Message}");
            }
        }
    }

    public static List<OperationLog> QueryLogs(string accountName = null, DateTime? startTime = null, DateTime? endTime = null, int limit = 100)
    {
        return QueryLogs(accountName, startTime, endTime, null, null, 0, limit);
    }

    public static List<OperationLog> QueryLogs(string accountName = null, DateTime? startTime = null, DateTime? endTime = null, int skip = 0, int limit = 100)
    {
        return QueryLogs(accountName, startTime, endTime, null, null, skip, limit);
    }

    public static List<OperationLog> QueryLogs(string accountName = null, DateTime? startTime = null, DateTime? endTime = null, string operationType = null, string userName = null, int skip = 0, int limit = 100)
    {
        lock (_lock)
        {
            try
            {
                using (var db = new LiteDB.LiteDatabase(_dbPath))
                {
                    var collection = db.GetCollection<OperationLog>("OperationLogs");
                    var query = collection.Query();

                    if (!string.IsNullOrEmpty(accountName) || !string.IsNullOrEmpty(userName))
                    {
                        string searchText = !string.IsNullOrEmpty(accountName) ? accountName : userName;
                        query = query.Where(x => x.AccountName.Contains(searchText) || x.UserName.Contains(searchText));
                    }

                    if (!string.IsNullOrEmpty(operationType))
                    {
                        query = query.Where(x => x.Operation == operationType);
                    }

                    if (startTime.HasValue)
                    {
                        query = query.Where(x => x.CreateTime >= startTime.Value);
                    }

                    if (endTime.HasValue)
                    {
                        query = query.Where(x => x.CreateTime <= endTime.Value);
                    }

                    return query.OrderByDescending(x => x.Timestamp).Skip(skip).Limit(limit).ToList();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"操作日志查询失败：{ex.Message}");
                return new List<OperationLog>();
            }
        }
    }

    public static List<OperationLog> GetAllLogs(int limit = 1000)
    {
        return QueryLogs(null, null, null, limit);
    }

    public static long GetLogCount()
    {
        return GetLogCount(null, null, null, null, null);
    }

    public static long GetLogCount(string accountName = null, DateTime? startTime = null, DateTime? endTime = null, string operationType = null, string userName = null)
    {
        lock (_lock)
        {
            try
            {
                using (var db = new LiteDB.LiteDatabase(_dbPath))
                {
                    var collection = db.GetCollection<OperationLog>("OperationLogs");
                    var query = collection.Query();

                    if (!string.IsNullOrEmpty(accountName) || !string.IsNullOrEmpty(userName))
                    {
                        string searchText = !string.IsNullOrEmpty(accountName) ? accountName : userName;
                        query = query.Where(x => x.AccountName.Contains(searchText) || x.UserName.Contains(searchText));
                    }

                    if (!string.IsNullOrEmpty(operationType))
                    {
                        query = query.Where(x => x.Operation == operationType);
                    }

                    if (startTime.HasValue)
                    {
                        query = query.Where(x => x.CreateTime >= startTime.Value);
                    }

                    if (endTime.HasValue)
                    {
                        query = query.Where(x => x.CreateTime <= endTime.Value);
                    }

                    return query.Count();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"操作日志计数失败：{ex.Message}");
                return 0;
            }
        }
    }

    public static bool ClearLogs()
    {
        lock (_lock)
        {
            try
            {
                using (var db = new LiteDB.LiteDatabase(_dbPath))
                {
                    db.DropCollection("OperationLogs");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"操作日志清空失败：{ex.Message}");
                return false;
            }
        }
    }
}