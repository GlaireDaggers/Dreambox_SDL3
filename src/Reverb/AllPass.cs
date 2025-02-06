using System.Runtime.CompilerServices;

public class AllPass
{
    public float feedback;

    private float[] buffer;
    private int bufferIdx;

    public AllPass(int bufferLength)
    {
        buffer = new float[bufferLength];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe float Process(float input)
    {
        float bufout = buffer[bufferIdx];

        // undenormalize
        uint v = *(uint*)&bufout;
        if ((v & 0x7f800000) == 0)
        {
            bufout = 0f;
        }

        float output = -input + bufout;
        buffer[bufferIdx++] = input + (bufout * feedback);
        bufferIdx %= buffer.Length;

        return output;
    }

    public void Mute()
    {
        Array.Fill(buffer, 0f);
    }
}