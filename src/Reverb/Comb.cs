using System.Runtime.CompilerServices;

public class Comb
{
    public float damp
    {
        get => damp1;
        set
        {
            damp1 = value;
            damp2 = 1f - value;
        }
    }

    public float feedback;

    private float[] buffer;
    private float damp1;
    private float damp2;
    private int bufferIdx;
    private float filterstore;

    public Comb(int bufferLength)
    {
        buffer = new float[bufferLength];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe float Process(float input)
    {
        float output = buffer[bufferIdx];

        // undenormalize output
        uint v = *(uint*)&output;
        if ((v & 0x7f800000) == 0)
        {
            output = 0f;
        }

        filterstore = (output * damp2) + (filterstore * damp1);

        // undenormalize filterstore
        float fs = filterstore;
        v = *(uint*)&fs;
        if ((v & 0x7f800000) == 0)
        {
            filterstore = 0f;
        }

        buffer[bufferIdx++] = input + (filterstore * feedback);
        bufferIdx %= buffer.Length;

        return output;
    }

    public void Mute()
    {
        Array.Fill(buffer, 0f);
    }
}