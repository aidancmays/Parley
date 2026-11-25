using NUnit.Framework;

namespace Parley.Tests
{
    public class VoiceBufferTests
    {
        static float[] Ramp(int n)
        {
            var a = new float[n];
            for (int i = 0; i < n; i++)
                a[i] = 0.5f;
            return a;
        }

        [Test]
        public void WaitsForStartThresholdBeforePlaying()
        {
            var b = new VoiceBuffer(1000, startSeconds: 0.1f, maxSeconds: 0.5f); // start at 100 samples
            b.Write(Ramp(50), 0, 50);
            var out1 = new float[10];
            b.Read(out1, 0, 10);
            Assert.AreEqual(0f, out1[9]);
            Assert.AreEqual(50, b.Count);

            b.Write(Ramp(60), 0, 60);
            b.Read(out1, 0, 10);
            Assert.IsTrue(b.Playing);
            Assert.Greater(out1[9], 0f);
            Assert.AreEqual(100, b.Count);
        }

        [Test]
        public void DropsOldAudioWhenTooMuchPilesUp()
        {
            var b = new VoiceBuffer(1000, startSeconds: 0.1f, maxSeconds: 0.3f);
            b.Write(Ramp(400), 0, 400);
            Assert.LessOrEqual(b.Count, b.MaxSamples);
            Assert.Greater(b.Dropped, 0);
        }

        [Test]
        public void RunsDryIntoSilenceAndWaitsAgain()
        {
            var b = new VoiceBuffer(1000, startSeconds: 0.01f, maxSeconds: 0.5f);
            b.Write(Ramp(20), 0, 20);
            var output = new float[30];
            b.Read(output, 0, 30);
            Assert.AreEqual(0f, output[29]);
            Assert.IsFalse(b.Playing);
            Assert.AreEqual(0, b.Count);
        }

        [Test]
        public void Pcm16ConvertsLittleEndianSigned()
        {
            var pcm = new byte[] { 0x00, 0x40, 0x00, 0xC0, 0xFF, 0x7F };
            var output = new float[3];
            int n = VoiceBuffer.Pcm16ToFloat(pcm, pcm.Length, output);
            Assert.AreEqual(3, n);
            Assert.AreEqual(0.5f, output[0], 1e-4f);
            Assert.AreEqual(-0.5f, output[1], 1e-4f);
            Assert.AreEqual(1f, output[2], 1e-3f);
        }
    }
}
