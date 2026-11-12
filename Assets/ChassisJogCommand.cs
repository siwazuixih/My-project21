using System;
using System.Globalization;

public enum ChassisJogDirection { Forward, Backward, Left, Right, Counterclockwise, Clockwise }

// 车体坐标：X正向前、Y正向左、角速度正向逆时针。界面mm/deg转为API的m/rad。
// 以下是本软件点动范围，不是厂家额定极限或安全认证。
public sealed class ChassisJogCommand
{
    private ChassisJogCommand() { }
    public ushort Api { get; private set; }
    public string Json { get; private set; }
    public string Description { get; private set; }

    public static ChassisJogCommand Create(ChassisJogDirection direction, double amount, double speed)
    {
        if (!Enum.IsDefined(typeof(ChassisJogDirection), direction)) throw new ArgumentException("未知底盘点动方向");
        bool turn = direction == ChassisJogDirection.Counterclockwise || direction == ChassisJogDirection.Clockwise;
        Check(amount, turn ? .1 : 1, turn ? 45 : 500, turn ? "单次转角(°)" : "单次距离(mm)");
        Check(speed, turn ? 1 : 10, turn ? 15 : 100, turn ? "角速度(°/s)" : "平移速度(mm/s)");
        string[] names = { "前进", "后退", "左移", "右移", "逆时针旋转", "顺时针旋转" };
        var command = new ChassisJogCommand { Api = (ushort)(turn ? 3056 : 3055),
            Description = names[(int)direction] + " " + Number(amount) + (turn ? "°" : "mm") +
                "，速度 " + Number(speed) + (turn ? "°/s" : "mm/s") };
        if (turn)
        {
            double sign = direction == ChassisJogDirection.Counterclockwise ? 1 : -1;
            command.Json = "{\"angle\":" + Number(amount * Math.PI / 180) + ",\"vw\":" + Number(sign * speed * Math.PI / 180) + ",\"mode\":0}";
        }
        else
        {
            double vx = direction == ChassisJogDirection.Forward ? speed / 1000 : direction == ChassisJogDirection.Backward ? -speed / 1000 : 0;
            double vy = direction == ChassisJogDirection.Left ? speed / 1000 : direction == ChassisJogDirection.Right ? -speed / 1000 : 0;
            command.Json = "{\"dist\":" + Number(amount / 1000) + ",\"vx\":" + Number(vx) + ",\"vy\":" + Number(vy) + ",\"mode\":0}";
        }
        return command;
    }
    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static void Check(double value, double min, double max, string name)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < min || value > max)
            throw new ArgumentException(name + "必须在 " + Number(min) + "～" + Number(max) + " 内且为有限数值");
    }
}
