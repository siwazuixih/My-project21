using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class RunManager
{
    public static Project Project { get; set; }

    public static RunType RunType { get; set; } = RunType.NONE;

    public static RunStatus RunStatus { get; set; } = RunStatus.IDLE;
}
