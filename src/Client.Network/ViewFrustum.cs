using System;
using System.Numerics;

namespace Client.Network
{
    /// <summary>
    /// The camera's view pyramid, expressed with plain vectors so target picking can ask "is this
    /// body on screen" without a Unity dependency and without a second, drifting copy of the
    /// projection maths.
    ///
    /// The customer's rule is "a target on screen is a target, wherever the cursor is": with one
    /// enemy visible, Space must pick it even if the cursor is in the far corner of the screen.
    /// That question can only be answered by the frustum, and it has to be answered the same way
    /// in the client and in the tests.
    /// </summary>
    public readonly struct ViewFrustum
    {
        private readonly Vector3 _position;
        private readonly Vector3 _right;
        private readonly Vector3 _up;
        private readonly Vector3 _forward;
        private readonly float _tanHalfVertical;
        private readonly float _aspect;
        private readonly float _near;

        /// <summary>
        /// Builds a frustum from the camera's basis and lens.
        /// </summary>
        /// <param name="position">Camera position in world space.</param>
        /// <param name="right">Normalised camera right vector.</param>
        /// <param name="up">Normalised camera up vector.</param>
        /// <param name="forward">Normalised camera forward vector.</param>
        /// <param name="verticalFieldOfViewDegrees">Vertical field of view, degrees.</param>
        /// <param name="aspect">Viewport width divided by height.</param>
        /// <param name="near">Near clip distance in metres.</param>
        public ViewFrustum(
            Vector3 position,
            Vector3 right,
            Vector3 up,
            Vector3 forward,
            float verticalFieldOfViewDegrees,
            float aspect,
            float near)
        {
            _position = position;
            _right = Normalise(right, Vector3.UnitX);
            _up = Normalise(up, Vector3.UnitY);
            _forward = Normalise(forward, -Vector3.UnitZ);
            _aspect = float.IsFinite(aspect) && aspect > 0f ? aspect : 1f;
            _near = float.IsFinite(near) && near > 0f ? near : 0f;

            // A lens outside (0, 179) is a broken camera, not a reason to hide every enemy:
            // fall back to a 90-degree view, which keeps "on screen" meaningful instead of
            // silently rejecting everything.
            var half = verticalFieldOfViewDegrees * 0.5f * (MathF.PI / 180f);
            var tan = MathF.Tan(half);
            _tanHalfVertical = float.IsFinite(tan) && tan > 0f ? tan : 1f;
        }

        /// <summary>Camera position the frustum was built from.</summary>
        public Vector3 Position => _position;

        /// <summary>
        /// True when any part of a sphere lies inside the view pyramid: in front of the near
        /// plane and within the four side planes, with the sphere's radius allowed to poke past
        /// them.
        ///
        /// A body is measured as a sphere on purpose. "Any part of the body is on screen" is the
        /// customer's definition of visible, and a sphere around the body is the cheapest honest
        /// version of it: it errs toward visible, so an enemy standing at the very edge of the
        /// frame stays selectable instead of becoming unreachable.
        /// </summary>
        /// <param name="center">Sphere centre in world space.</param>
        /// <param name="radius">Sphere radius in metres; negative is treated as zero.</param>
        public bool IsSphereVisible(Vector3 center, float radius)
        {
            if (!float.IsFinite(radius) || radius < 0f) radius = 0f;

            var offset = center - _position;
            var depth = Vector3.Dot(offset, _forward);

            // Entirely behind the near plane. depth + radius is the nearest point of the sphere.
            if (depth + radius <= _near) return false;

            // Half-extents of the pyramid at the sphere's depth. Clamping to the near plane keeps
            // a sphere that straddles the camera plane from producing a zero-sized window.
            var at = MathF.Max(depth, _near);
            var halfVertical = at * _tanHalfVertical;
            var halfHorizontal = halfVertical * _aspect;

            return MathF.Abs(Vector3.Dot(offset, _right)) - radius <= halfHorizontal
                && MathF.Abs(Vector3.Dot(offset, _up)) - radius <= halfVertical;
        }

        private static Vector3 Normalise(Vector3 value, Vector3 fallback)
        {
            var length = value.Length();
            if (!float.IsFinite(length) || length < 0.0001f) return fallback;
            return value / length;
        }
    }
}
