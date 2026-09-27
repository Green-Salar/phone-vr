using System;
using System.Numerics;

namespace PhoneVr
{
    /// <summary>
    /// Converts a phone DeviceOrientation event into a Unity rotation.
    /// The math follows three.js DeviceOrientationControls, then maps that
    /// right-handed camera (looks down -Z) into Unity (looks down +Z).
    /// </summary>
    public static class PhoneOrientation
    {
        public static Quaternion ToUnity(float alphaDeg, float betaDeg, float gammaDeg, float screenDeg, bool pointWithTop)
        {
            float alpha = alphaDeg * (MathF.PI / 180f);
            float beta = betaDeg * (MathF.PI / 180f);
            float gamma = gammaDeg * (MathF.PI / 180f);
            float orient = screenDeg * (MathF.PI / 180f);

            Quaternion q = FromEulerYXZ(beta, alpha, -gamma);
            if (!pointWithTop)
            {
                // -90 degrees around X: look out the back of the phone, not the top.
                var outTheBack = new Quaternion(-MathF.Sqrt(0.5f), 0f, 0f, MathF.Sqrt(0.5f));
                q = Quaternion.Multiply(q, outTheBack);
            }

            if (MathF.Abs(orient) > 0.0001f)
            {
                Quaternion screen = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -orient);
                q = Quaternion.Multiply(q, screen);
            }

            Vector3 lookRh = Rotate(q, new Vector3(0f, 0f, -1f));
            Vector3 upRh = Rotate(q, Vector3.UnitY);
            Vector3 look = new Vector3(lookRh.X, lookRh.Y, -lookRh.Z);
            Vector3 up = new Vector3(upRh.X, upRh.Y, -upRh.Z);
            if (look.LengthSquared() < 1e-8f)
                return Quaternion.Identity;
            return LookRotation(look, up);
        }

        public static Quaternion LookRotation(Vector3 forward, Vector3 up)
        {
            forward = Vector3.Normalize(forward);
            Vector3 right = Vector3.Cross(up, forward);
            if (right.LengthSquared() < 1e-8f)
            {
                Vector3 fallback = MathF.Abs(forward.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
                right = Vector3.Cross(fallback, forward);
            }
            right = Vector3.Normalize(right);
            Vector3 orthoUp = Vector3.Cross(forward, right);

            var m = new Matrix4x4(
                right.X, right.Y, right.Z, 0f,
                orthoUp.X, orthoUp.Y, orthoUp.Z, 0f,
                forward.X, forward.Y, forward.Z, 0f,
                0f, 0f, 0f, 1f);
            return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(m));
        }

        static Quaternion FromEulerYXZ(float x, float y, float z)
        {
            float c1 = MathF.Cos(x * 0.5f);
            float c2 = MathF.Cos(y * 0.5f);
            float c3 = MathF.Cos(z * 0.5f);
            float s1 = MathF.Sin(x * 0.5f);
            float s2 = MathF.Sin(y * 0.5f);
            float s3 = MathF.Sin(z * 0.5f);
            return new Quaternion(
                s1 * c2 * c3 + c1 * s2 * s3,
                c1 * s2 * c3 - s1 * c2 * s3,
                c1 * c2 * s3 - s1 * s2 * c3,
                c1 * c2 * c3 + s1 * s2 * s3);
        }

        static Vector3 Rotate(Quaternion q, Vector3 v)
        {
            var qv = new Quaternion(v.X, v.Y, v.Z, 0f);
            Quaternion r = Quaternion.Multiply(Quaternion.Multiply(q, qv), Quaternion.Conjugate(q));
            return new Vector3(r.X, r.Y, r.Z);
        }
    }
}
