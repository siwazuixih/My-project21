using System;
using System.IO;
using System.Threading.Tasks;

public partial class DobotController
{
    private async Task<int> ReadExactAsync(Stream stream, byte[] buffer, int length)
    {
        int total = 0;
        while (total < length)
        {
            int read = await stream.ReadAsync(buffer, total, length - total).ConfigureAwait(false);
            if (read <= 0) break;
            total += read;
        }
        return total;
    }
    // Counters are lifetime totals for this controller instance; snapshots include connection generation.
    private long receivedPackets, acceptedPackets, badHeaders, oldTimestamps, delayedPackets, backlogPackets, feedbackGeneration;
    private double lastPacketUnixSeconds;
    private string feedbackReadError = "none";
    private string feedbackEndpoint = "not_connected";

    private void RecordFeedbackPacket(bool discardedPrevious)
    {
        lock (feedbackLock)
        {
            receivedPackets++;
            if (discardedPrevious) backlogPackets++;
            lastPacketUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        }
    }

    private string FeedbackDiagnosticCounters()
    {
        lock (feedbackLock)
            return $"connection={feedbackGeneration} endpoint={feedbackEndpoint} rx_total={receivedPackets} accepted_total={acceptedPackets} bad_header_total={badHeaders} old_timestamp_total={oldTimestamps} delayed_total={delayedPackets} backlog_total={backlogPackets} last_rx_unix={Number(lastPacketUnixSeconds)} read_error={feedbackReadError}";
    }

    private void ParseFeedbackPacket(byte[] packet)
    {
        if (!BitConverter.IsLittleEndian || packet.Length != 1440) throw new IOException("反馈要求1440字节和小端平台");
        lock (feedbackLock)
        {
            if (BitConverter.ToUInt16(packet, 0) != 1440 || BitConverter.ToUInt64(packet, 48) != 0x123456789ABCDEFUL)
            { badHeaders++; return; }
            ulong timestamp = BitConverter.ToUInt64(packet, 32);
            if (protocolFeedbackSequence > 0 && timestamp <= robotTimestamp) { oldTimestamps++; return; }
            double offset = System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency - timestamp / 1000.0;
            feedbackClockOffset = Math.Min(feedbackClockOffset, offset);
            // Existing protection unchanged. Counters reveal received-but-rejected data.
            if (offset - feedbackClockOffset > 0.25) { delayedPackets++; lastFeedbackUnixSeconds = 0; return; }
            acceptedPackets++;
            robotTimestamp = timestamp;
            protocolFeedbackSequence++;
            currentCommandId = BitConverter.ToUInt64(packet, 1112);
            actualUser = packet[1012]; actualTool = packet[1013]; sixForceOnline = packet[1037];
            EnsureFeedbackArrays();
            currentRobotMode = (int)BitConverter.ToUInt64(packet, 24);
            currentRobotModeText = RobotModeToText(currentRobotMode);
            speedScaling = BitConverter.ToDouble(packet, 64);
            velocityRatio = packet[1016];
            robotError = packet[1029]; robotCollision = packet[1038];
            for (int i = 0; i < 6; i++)
            {
                actualForce[i] = BitConverter.ToDouble(packet, 576 + i * 8);
                actualToolPose[i] = BitConverter.ToDouble(packet, 624 + i * 8);
                actualJoints[i] = BitConverter.ToDouble(packet, 432 + i * 8);
                actualJointSpeeds[i] = BitConverter.ToDouble(packet, 480 + i * 8);
                protocolJointSpeeds[i] = actualJointSpeeds[i];
            }
            feedbackSequence++;
            lastFeedbackUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        }
    }
}
