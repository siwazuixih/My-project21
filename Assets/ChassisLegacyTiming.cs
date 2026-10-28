using System;

// 旧版等待公式，单位秒；它估计耗时，不提供任何实际到位证据。
[Serializable]
public sealed class ChassisLegacyTiming
{
    public float commandBufferSeconds = .5f;
    public float rotationBufferSeconds = 5;
    public float translationBufferSeconds = 5;
    public float minWaitSeconds = .2f;
    public float maxWaitSeconds = 0; // 0表示不设上限；不允许用上限截短已估计的运动时间。

    public ChassisLegacyTiming Copy() => new ChassisLegacyTiming {
        commandBufferSeconds = commandBufferSeconds, rotationBufferSeconds = rotationBufferSeconds,
        translationBufferSeconds = translationBufferSeconds, minWaitSeconds = minWaitSeconds, maxWaitSeconds = maxWaitSeconds };
    public void Validate()
    {
        foreach (float value in new[] { commandBufferSeconds, rotationBufferSeconds, translationBufferSeconds, minWaitSeconds, maxWaitSeconds })
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0 || value > 600)
                throw new ArgumentException("底盘旧模式等待参数必须是0～600秒的有限值");
        if (maxWaitSeconds > 0 && maxWaitSeconds < minWaitSeconds)
            throw new ArgumentException("底盘旧模式最大等待不能小于最小等待");
    }
    public double RotationSeconds(double radians, double speedRadS) => Estimate(radians, speedRadS, rotationBufferSeconds);
    public double TranslationSeconds(double metres, double speedMS) => Estimate(metres, speedMS, translationBufferSeconds);
    private double Estimate(double amount, double speed, double buffer)
    {
        Validate();
        if (double.IsNaN(amount) || double.IsInfinity(amount) || amount < 0 ||
            double.IsNaN(speed) || double.IsInfinity(speed) || speed <= 0)
            throw new ArgumentException("底盘旧模式距离/角度/速度无效");
        double wait = Math.Max(minWaitSeconds, amount / speed + commandBufferSeconds + buffer);
        if (wait > 600 || (maxWaitSeconds > 0 && wait > maxWaitSeconds))
            throw new ArgumentException("底盘旧模式预计等待超过上限，不能截短等待；请核对路径/速度，maxWaitSeconds=0表示不设配置上限");
        return wait;
    }
}
