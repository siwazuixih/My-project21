using System;
using System.Collections.Generic;

// Display-only storage. Never suppress Unity logging or robot feedback reception.
internal sealed class RuntimeConsoleLogBuffer
{
    private sealed class Entry
    {
        public string Line;
        public bool RobotFeedback;
    }

    private readonly List<Entry> entries = new List<Entry>();
    private int feedbackCount;
    private int otherCount;

    public int Count => entries.Count;

    public static bool IsRoutineRobotFeedback(string message, bool normalLog)
    {
        if (!normalLog || string.IsNullOrEmpty(message)) return false;
        // Match Simulation.HandleFeedbackUpdated, not every message mentioning feedback.
        const string prefix = "反馈更新:";
        const string alternatePrefix = "反馈更新：";
        int offset;
        if (message.StartsWith(prefix, StringComparison.Ordinal)) offset = prefix.Length;
        else if (message.StartsWith(alternatePrefix, StringComparison.Ordinal)) offset = alternatePrefix.Length;
        else return false;
        return message.Substring(offset).TrimStart().StartsWith("Mode=", StringComparison.Ordinal);
    }

    public void Append(string line, bool robotFeedback, int maxLines)
    {
        entries.Add(new Entry { Line = line ?? "", RobotFeedback = robotFeedback });
        if (robotFeedback) feedbackCount++;
        else otherCount++;
        Trim(Math.Max(1, maxLines));
    }

    private void Trim(int limit)
    {
        // Give each category a bounded budget so feedback cannot evict useful messages.
        for (int i = 0; i < entries.Count && (feedbackCount > limit || otherCount > limit);)
        {
            bool feedback = entries[i].RobotFeedback;
            if ((feedback && feedbackCount > limit) || (!feedback && otherCount > limit))
            {
                entries.RemoveAt(i);
                if (feedback) feedbackCount--;
                else otherCount--;
            }
            else i++;
        }
    }

    public string Render(bool hideRobotFeedback, int maxLines)
    {
        int limit = Math.Max(1, maxLines);
        Trim(limit);
        var visible = new List<string>();
        for (int i = entries.Count - 1; i >= 0 && visible.Count < limit; i--)
            if (!hideRobotFeedback || !entries[i].RobotFeedback) visible.Add(entries[i].Line);
        visible.Reverse();
        return string.Join("\n", visible);
    }
}
