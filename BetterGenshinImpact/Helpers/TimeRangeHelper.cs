using System;
using BetterGenshinImpact.GameTask;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Helpers;

public static class TimeRangeHelper
{
 
    /// <summary>
    /// 判断当前时间是否在指定时间范围内
    /// </summary>
    /// <param name="input">时间范围表达式，支持：单个小时、小时:分钟、范围段（纯数字端点为整点小时段：如 1-2 含 1:00:00–2:59:59）、多个条件逗号分隔</param>
    /// <returns>是否在时间范围内</returns>
    public static bool IsInTimeRange(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            LogDebug("输入为空");
            return false;
        }

        // 按逗号分割多个条件
        var conditions = input.Split(',', StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var condition in conditions)
        {
            var trimmedCondition = condition.Trim();
            
            if (string.IsNullOrWhiteSpace(trimmedCondition))
                continue;
                
            // 如果是单个数字（小时）
            if (IsSingleHour(trimmedCondition))
            {
                if (IsInSingleHour(trimmedCondition))
                {
                    LogDebug($"条件 '{trimmedCondition}' 满足");
                    return true;
                }
                continue;
            }
            
            // 如果是时间点（小时:分钟）
            if (IsTimePoint(trimmedCondition))
            {
                if (IsAtTimePoint(trimmedCondition))
                {
                    LogDebug($"条件 '{trimmedCondition}' 满足");
                    return true;
                }
                continue;
            }
            
            // 如果是时间范围
            if (IsTimeRange(trimmedCondition))
            {
                if (IsInTimeRangeInternal(trimmedCondition))
                {
                    LogDebug($"条件 '{trimmedCondition}' 满足");
                    return true;
                }
                continue;
            }
            
            LogDebug($"无法解析的条件: '{trimmedCondition}'，跳过");
        }
        
        LogDebug($"所有条件都不满足: '{input}'");
        return false;
    }
    
    /// <summary>
    /// 判断是否为单个小时
    /// </summary>
    private static bool IsSingleHour(string input)
    {
        return int.TryParse(input, out int hour) && hour >= 0 && hour <= 23;
    }
    
    /// <summary>
    /// 判断是否在单个小时范围内
    /// </summary>
    private static bool IsInSingleHour(string input)
    {
        if (int.TryParse(input, out int hour))
        {
            return DateTime.Now.Hour == hour;
        }
        return false;
    }
    
    /// <summary>
    /// 判断是否为时间点（小时:分钟）
    /// </summary>
    private static bool IsTimePoint(string input)
    {
        var parts = input.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
            return false;
            
        if (!int.TryParse(parts[0], out int hour) || hour < 0 || hour > 23)
            return false;
            
        if (!int.TryParse(parts[1], out int minute) || minute < 0 || minute > 59)
            return false;
            
        return true;
    }
    
    /// <summary>
    /// 判断是否在特定时间点
    /// </summary>
    private static bool IsAtTimePoint(string input)
    {
        var parts = input.Split(':');
        if (parts.Length != 2)
            return false;
            
        if (!int.TryParse(parts[0], out int hour) || !int.TryParse(parts[1], out int minute))
            return false;
            
        var now = DateTime.Now;
        return now.Hour == hour && now.Minute == minute;
    }
    
    /// <summary>
    /// 判断是否为时间范围
    /// </summary>
    private static bool IsTimeRange(string input)
    {
        return input.Contains("-");
    }
    
    /// <summary>
    /// 判断是否在时间范围内
    /// </summary>
    private static bool IsInTimeRangeInternal(string input)
    {
        var parts = input.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            LogDebug($"无效的时间范围格式: '{input}'");
            return false;
        }
        
        var startStr = parts[0].Trim();
        var endStr = parts[1].Trim();
        
        if (!TryParseRangeEndpoint(startStr, isEnd: false, out int startSeconds, out string errorMsg))
        {
            LogDebug($"解析开始时间失败: {errorMsg}");
            return false;
        }
        
        if (!TryParseRangeEndpoint(endStr, isEnd: true, out int endSeconds, out errorMsg))
        {
            LogDebug($"解析结束时间失败: {errorMsg}");
            return false;
        }
        
        var now = DateTime.Now;
        int currentSeconds = now.Hour * 3600 + now.Minute * 60 + now.Second;
        
        // 跨天：起点时刻晚于终点（如 23:50-4 含次日 4:59:59）
        if (startSeconds > endSeconds)
        {
            return currentSeconds >= startSeconds || currentSeconds <= endSeconds;
        }

        return currentSeconds >= startSeconds && currentSeconds <= endSeconds;
    }

    /// <summary>
    /// 解析范围端点为「从 0 点起的秒数」：纯数字小时起点为 h:00:00，终点为 h:59:59；HH:mm 起点为该分钟 :00，终点为该分钟 :59。
    /// </summary>
    private static bool TryParseRangeEndpoint(string timeStr, bool isEnd, out int secondsFromMidnight, out string errorMessage)
    {
        secondsFromMidnight = 0;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(timeStr))
        {
            errorMessage = "时间字符串为空";
            return false;
        }

        var t = timeStr.Trim();
        if (!t.Contains(':') && int.TryParse(t, out int hour) && hour is >= 0 and <= 23)
        {
            secondsFromMidnight = isEnd ? hour * 3600 + 59 * 60 + 59 : hour * 3600;
            return true;
        }

        if (!TryParseTimeToMinutes(t, out int minuteOfDay, out errorMessage))
            return false;

        secondsFromMidnight = minuteOfDay * 60 + (isEnd ? 59 : 0);
        return true;
    }
    
    /// <summary>
    /// 将时间字符串转换为分钟数
    /// </summary>
    private static bool TryParseTimeToMinutes(string timeStr, out int minutes, out string errorMessage)
    {
        minutes = 0;
        errorMessage = null;
        
        if (string.IsNullOrWhiteSpace(timeStr))
        {
            errorMessage = "时间字符串为空";
            return false;
        }
        
        // 如果只有数字，表示整点
        if (int.TryParse(timeStr, out int hour))
        {
            if (hour >= 0 && hour <= 23)
            {
                minutes = hour * 60;
                return true;
            }
            else
            {
                errorMessage = $"小时值超出范围: {hour}";
                return false;
            }
        }
        
        // 解析小时:分钟格式
        var parts = timeStr.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2)
        {
            if (!int.TryParse(parts[0].Trim(), out int hourPart))
            {
                errorMessage = $"无法解析小时部分: {parts[0]}";
                return false;
            }
            
            if (!int.TryParse(parts[1].Trim(), out int minutePart))
            {
                errorMessage = $"无法解析分钟部分: {parts[1]}";
                return false;
            }
            
            if (hourPart < 0 || hourPart > 23)
            {
                errorMessage = $"小时值超出范围: {hourPart}";
                return false;
            }
            
            if (minutePart < 0 || minutePart > 59)
            {
                errorMessage = $"分钟值超出范围: {minutePart}";
                return false;
            }
            
            minutes = hourPart * 60 + minutePart;
            return true;
        }
        
        errorMessage = $"无法解析时间格式: {timeStr}";
        return false;
    }
    
    /// <summary>
    /// 日志记录方法
    /// </summary>
    private static void LogDebug(string message)
    {
        // 这里可以根据需要修改为实际的日志记录方式
        // 例如：使用 ILogger、Console.WriteLine、Debug.WriteLine 等
        //Console.WriteLine($"[TimeRangeHelper] {DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}");
        
    }
}