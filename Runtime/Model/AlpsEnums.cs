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

    /// <summary>
    /// Mode: what the shared phase generator produces. A wave splits each cycle into a rise,
    /// a high hold, a fall and a low hold. Random is a smooth seeded wander.
    /// </summary>
    public enum AlpsPhaseMode
    {
        Wave,
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

    /// <summary>
    /// Arrangement shape: the path a container places its children on, or off to leave them
    /// alone. An arc is a circle whose sweep is under a full turn. Order matches
    /// <see cref="AlpsArrangementEvaluator"/>.
    /// </summary>
    public enum AlpsArrangementShape
    {
        Off,
        Line,
        Circle,
        Polygon,
        Rectangle,
        Grid,
    }

    /// <summary>
    /// Arrangement spacing: ends puts the first and last slot on the ends of an open path,
    /// centered gives every slot an equal cell and sits it in the middle of it.
    /// </summary>
    public enum AlpsArrangementSpacing
    {
        Ends,
        Centered,
    }

    /// <summary>Arrangement facing: which way each slot's +Z points before its own rotation.</summary>
    public enum AlpsArrangementFacing
    {
        Keep,
        Outward,
        Inward,
        Along,
        Target,
    }

    /// <summary>Easing functions offered as thumbnails. Order drives the tile grid and matches <see cref="AlpsPhaseCurve.Ease"/>.</summary>
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
        InCubic,
        OutCubic,
        InOutQuad,
        InBack,
        InOutBack,
        InBounce,
        InOutBounce,
    }
}
