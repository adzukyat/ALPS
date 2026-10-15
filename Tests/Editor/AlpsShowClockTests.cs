using NUnit.Framework;
using UnityEngine;

namespace AdzukiSoft.ALPS.Tests
{
    /// <summary>
    /// The show player's own clock (<see cref="AlpsShowPlayer.FollowDirector"/>), which keeps the
    /// lights moving every frame when a sync system writes a video player's time, stepping once
    /// per video frame, into the director every frame.
    /// </summary>
    public class AlpsShowClockTests
    {
        private const float FrameSeconds = 1f / 90f;
        private const float VideoFrameSeconds = 1f / 30f;

        private GameObject _host;
        private AlpsShowPlayer _player;

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("Clock");
            _player = _host.AddComponent<AlpsShowPlayer>();
        }

        [TearDown]
        public void CleanUp()
        {
            Object.DestroyImmediate(_host);
        }

        [Test]
        public void VideoTime_MovesEveryFrameAndStaysOnTheVideo()
        {
            var previous = float.NegativeInfinity;
            for (var frame = 0; frame < 900; frame++)
            {
                var now = 10f + frame * FrameSeconds;
                var trueTime = frame * FrameSeconds;
                var videoTime = Mathf.Floor(trueTime / VideoFrameSeconds) * VideoFrameSeconds;
                var drawn = _player.FollowDirector(videoTime, now, FrameSeconds);
                if (frame > 0)
                {
                    Assert.Greater(drawn, previous, "frame " + frame);
                }

                if (frame > 90)
                {
                    Assert.AreEqual(trueTime, drawn, VideoFrameSeconds, "frame " + frame);
                }

                previous = drawn;
            }
        }

        [Test]
        public void SmoothDirector_IsDrawnAsItIs()
        {
            for (var frame = 0; frame < 900; frame++)
            {
                var time = 3f + frame * FrameSeconds;
                Assert.AreEqual(time, _player.FollowDirector(time, 10f + frame * FrameSeconds, FrameSeconds), 1e-4f, "frame " + frame);
            }
        }

        [Test]
        public void Seek_JumpsAtOnce()
        {
            for (var frame = 0; frame < 30; frame++)
            {
                _player.FollowDirector(frame * FrameSeconds, 10f + frame * FrameSeconds, FrameSeconds);
            }

            Assert.AreEqual(60f, _player.FollowDirector(60f, 10f + 30 * FrameSeconds, FrameSeconds));
            Assert.AreEqual(1f, _player.FollowDirector(1f, 10f + 31 * FrameSeconds, FrameSeconds));
        }

        [Test]
        public void Pause_StopsOnTheDirectorsTime()
        {
            var frame = 0;
            for (; frame < 30; frame++)
            {
                _player.FollowDirector(frame * FrameSeconds, 10f + frame * FrameSeconds, FrameSeconds);
            }

            const float paused = 29 * FrameSeconds;
            var stallFrames = Mathf.CeilToInt(AlpsShowPlayer.ClockStallSeconds / FrameSeconds) + 1;
            for (var i = 0; i < stallFrames; i++, frame++)
            {
                _player.FollowDirector(paused, 10f + frame * FrameSeconds, FrameSeconds);
            }

            for (var i = 0; i < 10; i++, frame++)
            {
                Assert.AreEqual(paused, _player.FollowDirector(paused, 10f + frame * FrameSeconds, FrameSeconds));
            }
        }
    }
}
