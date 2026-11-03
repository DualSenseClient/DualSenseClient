namespace DualSenseClient.Controllers.DualSense.Feature;

/// <summary>
/// Motion-sensor calibration parsed from feature report 0x05.
/// The report carries seventeen signed 16-bit little-endian words at wire offsets 1-34:
/// three gyro biases, six gyro plus/minus calibration words, two gyro speed words and
/// six accelerometer plus/minus words.
/// </summary>
public readonly struct CalibrationInfo
{
    /// <summary>
    /// The raw 0x05 feature report buffer this struct reads from.
    /// </summary>
    private readonly byte[] _raw;

    /// <summary>
    /// Creates a new calibration view over a raw feature report buffer.
    /// </summary>
    /// <param name="raw">The raw 0x05 feature report buffer.</param>
    public CalibrationInfo(byte[] raw)
    {
        _raw = raw;
    }

    /// <summary>
    /// Whether the buffer is long enough to carry the seventeen calibration words and
    /// starts with report ID 0x05.
    /// </summary>
    public bool IsValid
    {
        get
        {
            return _raw.Length >= 35 && _raw[0] == 0x05;
        }
    }

    /// <summary>Gyroscope pitch bias (wire offset 1).</summary>
    public short GyroPitchBias
    {
        get
        {
            return GetInt16(1);
        }
    }

    /// <summary>Gyroscope yaw bias (wire offset 3).</summary>
    public short GyroYawBias
    {
        get
        {
            return GetInt16(3);
        }
    }

    /// <summary>Gyroscope roll bias (wire offset 5).</summary>
    public short GyroRollBias
    {
        get
        {
            return GetInt16(5);
        }
    }

    /// <summary>Gyroscope pitch plus calibration point (wire offset 7).</summary>
    public short GyroPitchPlus
    {
        get
        {
            return GetInt16(7);
        }
    }

    /// <summary>Gyroscope pitch minus calibration point (wire offset 9).</summary>
    public short GyroPitchMinus
    {
        get
        {
            return GetInt16(9);
        }
    }

    /// <summary>Gyroscope yaw plus calibration point (wire offset 11).</summary>
    public short GyroYawPlus
    {
        get
        {
            return GetInt16(11);
        }
    }

    /// <summary>Gyroscope yaw minus calibration point (wire offset 13).</summary>
    public short GyroYawMinus
    {
        get
        {
            return GetInt16(13);
        }
    }

    /// <summary>Gyroscope roll plus calibration point (wire offset 15).</summary>
    public short GyroRollPlus
    {
        get
        {
            return GetInt16(15);
        }
    }

    /// <summary>Gyroscope roll minus calibration point (wire offset 17).</summary>
    public short GyroRollMinus
    {
        get
        {
            return GetInt16(17);
        }
    }

    /// <summary>Gyroscope speed plus word (wire offset 19).</summary>
    public short GyroSpeedPlus
    {
        get
        {
            return GetInt16(19);
        }
    }

    /// <summary>Gyroscope speed minus word (wire offset 21).</summary>
    public short GyroSpeedMinus
    {
        get
        {
            return GetInt16(21);
        }
    }

    /// <summary>Accelerometer X plus calibration point (wire offset 23).</summary>
    public short AccelXPlus
    {
        get
        {
            return GetInt16(23);
        }
    }

    /// <summary>Accelerometer X minus calibration point (wire offset 25).</summary>
    public short AccelXMinus
    {
        get
        {
            return GetInt16(25);
        }
    }

    /// <summary>Accelerometer Y plus calibration point (wire offset 27).</summary>
    public short AccelYPlus
    {
        get
        {
            return GetInt16(27);
        }
    }

    /// <summary>Accelerometer Y minus calibration point (wire offset 29).</summary>
    public short AccelYMinus
    {
        get
        {
            return GetInt16(29);
        }
    }

    /// <summary>Accelerometer Z plus calibration point (wire offset 31).</summary>
    public short AccelZPlus
    {
        get
        {
            return GetInt16(31);
        }
    }

    /// <summary>Accelerometer Z minus calibration point (wire offset 33).</summary>
    public short AccelZMinus
    {
        get
        {
            return GetInt16(33);
        }
    }

    /// <summary>
    /// Reads a signed 16-bit little-endian word, or zero when the report is invalid.
    /// </summary>
    /// <param name="offset">Wire offset of the word.</param>
    private short GetInt16(int offset) => IsValid ? (short)(_raw[offset] | (_raw[offset + 1] << 8)) : (short)0;
}