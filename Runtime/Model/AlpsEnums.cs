namespace AdzukiSoft.ALPS
{
    /// <summary>Order: how fixture index maps to the normalized position p_i in 0..1.</summary>
    public enum AlpsOrderMode
    {
        Normal,
        Reverse,
        Symmetric,
        Random,
    }

    /// <summary>Mode: the waveform the shared phase generator produces.</summary>
    public enum AlpsPhaseMode
    {
        Forward,
        PingPong,
        Random,
    }

    /// <summary>Timing: whether a range/palette is swept inside one cycle or stepped per cycle.</summary>
    public enum AlpsTimingMode
    {
        WithinCycle,
        PerCycle,
    }

    /// <summary>Effect: the fixed catalog of effects a clip can carry.</summary>
    public enum AlpsEffectKind
    {
        Move,
        Cone,
        Color,
        Brightness,
        Flicker,
        Gobo,
    }

    /// <summary>Odd / even split: which half of the fixture order an effect applies to.</summary>
    public enum AlpsParity
    {
        All,
        Even,
        Odd,
    }

    /// <summary>Move tab selection.</summary>
    public enum AlpsMoveMode
    {
        Angle,
        Circle,
        TrackUser,
    }

    /// <summary>Easing functions offered as thumbnails. Order drives the tile grid and matches <see cref="AlpsShowEvaluator.Ease"/>.</summary>
    public enum AlpsEaseType
    {
        Linear,
        InSine,
        OutSine,
        InOutSine,
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
        InBack,
        InOutBack,
        InBounce,
        InOutBounce,
    }
}
