namespace ManeuverForVRC.Ui
{
    /// <summary>Order: how fixture index maps to the normalized position p_i in 0..1.</summary>
    public enum MfvOrderMode
    {
        Normal,
        Symmetric,
        Random,
    }

    /// <summary>Mode: the waveform the shared phase generator produces.</summary>
    public enum MfvPhaseMode
    {
        Forward,
        PingPong,
        Random,
    }

    /// <summary>Timing: whether a range/palette is swept inside one cycle or stepped per cycle.</summary>
    public enum MfvTimingMode
    {
        WithinCycle,
        PerCycle,
    }

    /// <summary>Effect: the fixed catalog of effects a clip can carry.</summary>
    public enum MfvEffectKind
    {
        Move,
        Cone,
        Color,
        Brightness,
        Flicker,
        Gobo,
    }

    /// <summary>Odd / even split: which half of the fixture order an effect applies to.</summary>
    public enum MfvParity
    {
        All,
        Even,
        Odd,
    }

    /// <summary>Move tab selection.</summary>
    public enum MfvMoveMode
    {
        Angle,
        TrackUser,
    }

    /// <summary>Easing functions offered as thumbnails. Order drives the tile grid.</summary>
    public enum MfvEaseType
    {
        Linear,
        InQuad,
        OutQuad,
        InOutCubic,
        InExpo,
        OutExpo,
        OutBack,
        OutBounce,
        Step,
        InCubic,
        OutCubic,
        InOutQuad,
        InSine,
        OutSine,
        InOutSine,
        InBack,
        InOutBack,
        InBounce,
        InOutBounce,
    }
}
