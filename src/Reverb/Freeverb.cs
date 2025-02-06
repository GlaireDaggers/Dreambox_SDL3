// adapted from https://github.com/sinshu/freeverb/
public class Freeverb
{
    private const float FIXED_GAIN = 0.015f;
    private const float SCALE_WET = 3f;
    private const float SCALE_DRY = 2f;
    private const float SCALE_DAMP = 0.4f;
    private const float SCALE_ROOM = 0.28f;
    private const float OFFSET_ROOM = 0.7f;
    private const float INITIAL_ROOM = 0.5f;
    private const float INITIAL_DAMP = 0.5f;
    private const float INITIAL_WET = 1f / SCALE_WET;
    private const float INITIAL_DRY = 0f;
    private const float INITIAL_WIDTH = 1f;
    private const float INITIAL_MODE = 0f;
    private const float FREEZE_MODE = 0.5f;
    private const int STEREO_SPREAD = 23;

    // These values assume 44.1KHz sample rate
    // they will probably be OK for 48KHz sample rate
    // but would need scaling for 96KHz (or other) sample rates.
    // The values were obtained by listening tests.

    private static readonly int[] combtuning = new int[] {
        1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617
    };

    private static readonly int[] allpasstuning = new int[] {
        556, 441, 341, 225
    };

    public float RoomSize
    {
        get => (roomsize - OFFSET_ROOM) / SCALE_ROOM;
        set
        {
            roomsize = (value * SCALE_ROOM) + OFFSET_ROOM;
            Update();
        }
    }

    public float Damp
    {
        get => damp / SCALE_DAMP;
        set
        {
            damp = value * SCALE_DAMP;
            Update();
        }
    }

    public float Wet
    {
        get => wet / SCALE_WET;
        set
        {
            wet = value * SCALE_WET;
            Update();
        }
    }

    public float Dry
    {
        get => dry / SCALE_DRY;
        set
        {
            dry = value * SCALE_DRY;
            Update();
        }
    }

    public float Width
    {
        get => width;
        set
        {
            width = value;
            Update();
        }
    }

    public float Mode
    {
        get
        {
            // huh???
            if (mode >= FREEZE_MODE) return 1f;
            return 0f;
        }
        set
        {
            mode = value;
            Update();
        }
    }

    private Comb[] combL;
    private Comb[] combR;

    private AllPass[] allpassL;
    private AllPass[] allpassR;

    private float gain;
    private float roomsize, roomsize1;
    private float damp, damp1;
    private float wet, wet1, wet2;
    private float dry;
    private float width;
    private float mode;

    public Freeverb()
    {
        combL = new Comb[combtuning.Length];
        combR = new Comb[combtuning.Length];
        allpassL = new AllPass[allpasstuning.Length];
        allpassR = new AllPass[allpasstuning.Length];

        for (int i = 0; i < combtuning.Length; i++)
        {
            combL[i] = new Comb(combtuning[i]);
            combR[i] = new Comb(combtuning[i] + STEREO_SPREAD);
        }

        for (int i = 0; i < allpasstuning.Length; i++)
        {
            allpassL[i] = new AllPass(allpasstuning[i]);
            allpassR[i] = new AllPass(allpasstuning[i] + STEREO_SPREAD);
        }

        Wet = INITIAL_WET;
        RoomSize = INITIAL_ROOM;
        Dry = INITIAL_DRY;
        Damp = INITIAL_DAMP;
        Width = INITIAL_WIDTH;
        Mode = INITIAL_MODE;
    }

    public unsafe void ProcessMix(float* inputL, float* inputR, float* outputL, float* outputR, int numSamples, int skip)
    {
        float outL, outR, input;

        while (numSamples-- > 0)
        {
            outL = outR = 0f;
            input = (*inputL + *inputR) * gain;
            
            // Accumulate comb filters in parallel
            for (int j = 0; j < combtuning.Length; j++)
            {
                outL += combL[j].Process(input);
                outR += combR[j].Process(input);
            }

            // Feed through allpasses in series
            for (int j = 0; j < allpasstuning.Length; j++)
            {
                outL = allpassL[j].Process(outL);
                outR = allpassR[j].Process(outR);
            }

            // Calculate output MIXING with anything already there
            *outputL += outL * wet1 + outR * wet2 + *inputL * dry;
            *outputR += outR * wet1 + outL * wet2 + *inputR * dry;

            // Increment sample pointers, allowing for interleave (if any)
            inputL += skip;
            inputR += skip;
            outputL += skip;
            outputR += skip;
        }
    }

    public unsafe void ProcessReplace(float* inputL, float* inputR, float* outputL, float* outputR, int numSamples, int skip)
    {
        float outL, outR, input;

        while (numSamples-- > 0)
        {
            outL = outR = 0f;
            input = (*inputL + *inputR) * gain;

            // Accumulate comb filters in parallel
            for (int j = 0; j < combtuning.Length; j++)
            {
                outL += combL[j].Process(input);
                outR += combR[j].Process(input);
            }

            // Feed through allpasses in series
            for (int j = 0; j < allpasstuning.Length; j++)
            {
                outL = allpassL[j].Process(outL);
                outR = allpassR[j].Process(outR);
            }

            // Calculate output REPLACING anything already there
            *outputL = outL * wet1 + outR * wet2 + *inputL * dry;
            *outputR = outR * wet1 + outL * wet2 + *inputR * dry;

            // Increment sample pointers, allowing for interleave (if any)
            inputL += skip;
            inputR += skip;
            outputL += skip;
            outputR += skip;
        }
    }

    private void Update()
    {
        wet1 = wet * (width / 2 + 0.5f);
        wet2 = wet * ((1 - width) / 2);

        if (mode >= FREEZE_MODE)
        {
            roomsize1 = 1;
            damp1 = 0;
            gain = 0f;
        }
        else
        {
            roomsize1 = roomsize;
            damp1 = damp;
            gain = FIXED_GAIN;
        }

        for (int i = 0; i < combtuning.Length; i++)
        {
            combL[i].feedback = roomsize1;
            combR[i].feedback = roomsize1;
            combL[i].damp = damp1;
            combR[i].damp = damp1;
        }
    }
}