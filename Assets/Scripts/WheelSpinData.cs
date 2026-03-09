// WheelSpinData.cs
using UnityEngine;

public class WheelSpinData : MonoBehaviour
{
    public enum WheelType { Drive, Turn }

    /// <summary>1 = clockwise/forward, -1 = counter-clockwise/reverse</summary>
    public int spinDirection = 1;

    /// <summary>Drive wheels propel the vehicle. Turn wheels steer it.</summary>
    public WheelType wheelType = WheelType.Drive;
}