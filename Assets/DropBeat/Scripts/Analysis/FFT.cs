using System;

namespace DropBeat
{
    /// <summary>
    /// Small in-place radix-2 complex FFT with cached twiddles per size.
    /// </summary>
    public sealed class FFT
    {
        public readonly int Size;
        private readonly int[] _rev;
        private readonly float[] _cos;
        private readonly float[] _sin;

        public FFT(int size)
        {
            if (size < 2 || (size & (size - 1)) != 0)
            {
                throw new ArgumentException("FFT size must be a power of two.", nameof(size));
            }

            Size = size;
            int bits = 0;
            while ((1 << bits) < size) bits++;

            _rev = new int[size];
            for (int i = 0; i < size; i++)
            {
                int r = 0;
                for (int b = 0; b < bits; b++)
                {
                    if ((i & (1 << b)) != 0) r |= 1 << (bits - 1 - b);
                }
                _rev[i] = r;
            }

            _cos = new float[size / 2];
            _sin = new float[size / 2];
            for (int i = 0; i < size / 2; i++)
            {
                double a = -2.0 * Math.PI * i / size;
                _cos[i] = (float)Math.Cos(a);
                _sin[i] = (float)Math.Sin(a);
            }
        }

        /// <summary>Forward transform (inverse = true conjugates twiddles and scales by 1/N).</summary>
        public void Transform(float[] re, float[] im, bool inverse = false)
        {
            int n = Size;
            for (int i = 0; i < n; i++)
            {
                int j = _rev[i];
                if (j > i)
                {
                    float tr = re[i]; re[i] = re[j]; re[j] = tr;
                    float ti = im[i]; im[i] = im[j]; im[j] = ti;
                }
            }

            float sign = inverse ? -1f : 1f;
            for (int len = 2; len <= n; len <<= 1)
            {
                int half = len >> 1;
                int step = n / len;
                for (int start = 0; start < n; start += len)
                {
                    for (int k = 0; k < half; k++)
                    {
                        float wr = _cos[k * step];
                        float wi = sign * _sin[k * step];
                        int a = start + k;
                        int b = a + half;
                        float xr = re[b] * wr - im[b] * wi;
                        float xi = re[b] * wi + im[b] * wr;
                        re[b] = re[a] - xr;
                        im[b] = im[a] - xi;
                        re[a] += xr;
                        im[a] += xi;
                    }
                }
            }

            if (inverse)
            {
                float s = 1f / n;
                for (int i = 0; i < n; i++)
                {
                    re[i] *= s;
                    im[i] *= s;
                }
            }
        }

        public static int NextPow2(int v)
        {
            int p = 1;
            while (p < v) p <<= 1;
            return p;
        }
    }
}
