using System;
using System.Collections.Generic;
using UnityEngine;

namespace Facecast
{
    /// <summary>Where an avatar stands and looks: feet position, body yaw and head pitch (degrees, positive looks down).</summary>
    [Serializable]
    public struct AvatarPose
    {
        public Vector3 Position;
        public float Yaw;
        public float Pitch;

        public AvatarPose(Vector3 position, float yaw, float pitch)
        {
            Position = position;
            Yaw = yaw;
            Pitch = pitch;
        }

        /// <summary>Blends two poses; yaw takes the short way round (350° to 10° passes through 0°).</summary>
        public static AvatarPose Lerp(AvatarPose a, AvatarPose b, float t) => new(
            Vector3.LerpUnclamped(a.Position, b.Position, t),
            Mathf.Repeat(a.Yaw + Mathf.DeltaAngle(a.Yaw, b.Yaw) * t, 360f),
            Mathf.LerpUnclamped(a.Pitch, b.Pitch, t));

        /// <summary>The wire format: x, y, z, yaw, pitch, then the sender's clock in seconds.</summary>
        public float[] ToWire(float senderTime) => new[] { Position.x, Position.y, Position.z, Yaw, Pitch, senderTime };

        public static bool TryFromWire(float[] p, out AvatarPose pose, out float senderTime)
        {
            pose = default;
            senderTime = 0f;
            if (p == null || p.Length < 6) return false;
            for (int i = 0; i < 6; i++)
                if (float.IsNaN(p[i]) || float.IsInfinity(p[i])) return false;
            pose = new AvatarPose(new Vector3(p[0], p[1], p[2]), Mathf.Repeat(p[3], 360f), Mathf.Clamp(p[4], -89f, 89f));
            senderTime = p[5];
            return true;
        }
    }

    /// <summary>
    /// A remote avatar's recent poses, played back a little in the past so it moves smoothly however unevenly the
    /// updates arrive. Poses carry the sender's clock; the buffer tracks the offset to the local clock (the smallest
    /// seen, so the fastest delivery, drifting up slowly in case the delay grows for good) and plays back
    /// <see cref="Delay"/> behind the newest pose it could have.
    /// </summary>
    public sealed class PoseBuffer
    {
        /// <summary>Playback delay: two updates at 15 Hz plus jitter.</summary>
        public const float Delay = 0.15f;
        /// <summary>When updates stop, keep going along the last movement for at most this long, then stand still.</summary>
        public const float MaxExtrapolation = 0.25f;
        /// <summary>How fast the clock offset may grow (seconds per second) when updates keep arriving late.</summary>
        const float OffsetCreep = 0.05f;
        const int Capacity = 32;

        readonly List<(float time, AvatarPose pose)> _samples = new();
        float _offset;
        float _lastReceive;

        public int Count => _samples.Count;
        public AvatarPose Latest => _samples.Count > 0 ? _samples[^1].pose : default;

        public void Add(float senderTime, float receiveTime, AvatarPose pose)
        {
            float offset = receiveTime - senderTime;
            if (_samples.Count == 0) _offset = offset;
            else _offset = Mathf.Min(offset, _offset + OffsetCreep * Mathf.Max(0f, receiveTime - _lastReceive));
            _lastReceive = receiveTime;

            if (_samples.Count > 0 && senderTime <= _samples[^1].time) return;  // stale or duplicate
            _samples.Add((senderTime, pose));
            if (_samples.Count > Capacity) _samples.RemoveAt(0);
        }

        /// <summary>The pose to show at local time <paramref name="now"/>.</summary>
        public bool TrySample(float now, out AvatarPose pose)
        {
            int n = _samples.Count;
            if (n == 0)
            {
                pose = default;
                return false;
            }
            float t = now - _offset - Delay;
            if (n == 1 || t <= _samples[0].time)
            {
                pose = _samples[0].pose;
                return true;
            }
            for (int i = 1; i < n; i++)
            {
                var (t1, p1) = _samples[i];
                if (t > t1) continue;
                var (t0, p0) = _samples[i - 1];
                pose = AvatarPose.Lerp(p0, p1, Mathf.InverseLerp(t0, t1, t));
                return true;
            }
            // Past the newest pose: continue along the last movement for a moment.
            var (ta, pa) = _samples[n - 2];
            var (tb, pb) = _samples[n - 1];
            float ahead = Mathf.Min(t - tb, MaxExtrapolation);
            pose = tb - ta > 1e-4f ? AvatarPose.Lerp(pa, pb, 1f + ahead / (tb - ta)) : pb;
            pose.Pitch = pb.Pitch;
            return true;
        }
    }
}
