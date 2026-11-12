using System;

static class Program
{
    static int passed;
    static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        passed++;
    }
    static void Main()
    {
        const string feedback = "反馈更新: Mode=已使能、空闲, SpeedScaling=3, VelocityRatio=0";
        Check(RuntimeConsoleLogBuffer.IsRoutineRobotFeedback(feedback, true), "matches production feedback");
        Check(RuntimeConsoleLogBuffer.IsRoutineRobotFeedback("反馈更新： Mode=运行中", true), "full-width colon");
        Check(!RuntimeConsoleLogBuffer.IsRoutineRobotFeedback(feedback, false), "warning/error never filtered");
        Check(!RuntimeConsoleLogBuffer.IsRoutineRobotFeedback("机械臂反馈失败: 连接中断", true), "connection failure retained");
        Check(!RuntimeConsoleLogBuffer.IsRoutineRobotFeedback("[StopTrace][WAIT] feedback_age=0.3", true), "stop diagnostic retained");
        Check(!RuntimeConsoleLogBuffer.IsRoutineRobotFeedback("[ToolTrace] {\"rx_bytes\":41,\"feedback_frames\":0}", true), "electric-tool diagnostic is not hidden by robot-feedback filter");
        Check(!RuntimeConsoleLogBuffer.IsRoutineRobotFeedback("检查机械臂反馈: Mode=5", true), "manual inspection retained");
        Check(!RuntimeConsoleLogBuffer.IsRoutineRobotFeedback("反馈更新: 电缸异常", true), "other feedback retained");
        Check(!RuntimeConsoleLogBuffer.IsRoutineRobotFeedback(null, true), "null safe");
        Check(!RuntimeConsoleLogBuffer.IsRoutineRobotFeedback("", true), "empty safe");

        var buffer = new RuntimeConsoleLogBuffer();
        buffer.Append("connected", false, 3);
        buffer.Append("feedback1", true, 3);
        buffer.Append("failed", false, 3);
        Check(buffer.Render(true, 3) == "connected\nfailed", "hide existing feedback immediately");
        Check(buffer.Render(false, 3) == "connected\nfeedback1\nfailed", "uncheck restores retained feedback in order");
        for (int i = 0; i < 1000; i++) buffer.Append("feedback" + i, true, 3);
        Check(buffer.Render(true, 3) == "connected\nfailed", "feedback flood cannot evict useful logs");
        Check(buffer.Count == 5, "feedback category bounded");
        Check(buffer.Render(false, 3) == "feedback997\nfeedback998\nfeedback999", "unfiltered display capped");
        for (int i = 0; i < 1000; i++) buffer.Append("normal" + i, false, 3);
        Check(buffer.Count == 6, "both categories bounded to twice maxLines");
        Check(buffer.Render(true, 3) == "normal997\nnormal998\nnormal999", "latest useful logs retained");
        Check(buffer.Render(false, 2) == "normal998\nnormal999" && buffer.Count == 4, "runtime limit reduction");
        buffer.Append("minimum", false, 0);
        Check(buffer.Render(true, 0) == "minimum" && buffer.Count <= 2, "zero limit clamped");
        buffer.Append("negative", false, -4);
        Check(buffer.Render(true, -4) == "negative", "negative limit clamped");

        var alternating = new RuntimeConsoleLogBuffer();
        alternating.Append("one", false, 4);
        alternating.Append("two", true, 4);
        alternating.Append("three", false, 4);
        alternating.Append("four", true, 4);
        Check(alternating.Render(false, 4) == "one\ntwo\nthree\nfour", "merged chronological order");
        Check(alternating.Render(true, 4) == "one\nthree", "filter only selected category");
        Check(alternating.Render(false, 4) == "one\ntwo\nthree\nfour", "filter does not destroy raw retained entries");
        Console.WriteLine($"{passed} runtime-console tests PASS (no Unity or network)");
    }
}
