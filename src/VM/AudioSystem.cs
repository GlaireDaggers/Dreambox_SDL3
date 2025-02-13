using System.Runtime.InteropServices;
using MeltySynth;
using NAudio.Wave;
using SDL3;

public enum AudioVoiceParameter
{
    Volume,
    Pitch,
    Detune,
    Pan,
    SampleData,
    Samplerate,
    LoopEnabled,
    LoopStart,
    LoopEnd,
    Reverb,
    FadeInDuration,
    FadeOutDuration,
    Start,
    Stop,
}

public enum AudioFormat
{
    PCM_S8,
    PCM_S16,
    IMA_ADPCM,
}

public enum AudioVoiceEnvelopeState
{
    Attack,
    Sustain,
    Release,
}

[StructLayout(LayoutKind.Explicit)]
public struct AudioParamEventValue
{
    [FieldOffset(0)]
    public int ivalue;

    [FieldOffset(0)]
    public float fvalue;
}

public struct AudioParamEvent
{
    public AudioVoice target;
    public AudioVoiceParameter parameter;
    public AudioParamEventValue value;
    public double time;
}

public class AudioParamEventQueue
{
    public int Count => _eventQueue.Count;

    private List<AudioParamEvent> _eventQueue = new List<AudioParamEvent>();

    public void Insert(AudioParamEvent eventData)
    {
        lock (_eventQueue)
        {
            for (int i = 0; i < _eventQueue.Count; i++)
            {
                if (_eventQueue[i].time > eventData.time)
                {
                    _eventQueue.Insert(i, eventData);
                    break;
                }
            }

            _eventQueue.Add(eventData);
        }
    }

    public bool TryPeek(out AudioParamEvent nextEvent)
    {
        lock (_eventQueue)
        {
            nextEvent = default(AudioParamEvent);
            if (_eventQueue.Count == 0) return false;
            nextEvent = _eventQueue[0];
            return true;
        }
    }

    public AudioParamEvent Dequeue()
    {
        lock (_eventQueue)
        {
            if (_eventQueue.Count == 0) throw new InvalidOperationException();

            var nextEvt = _eventQueue[0];
            _eventQueue.RemoveAt(0);
            return nextEvt;
        }
    }

    public bool TryDequeue(out AudioParamEvent nextEvent)
    {
        lock (_eventQueue)
        {
            nextEvent = default(AudioParamEvent);
            if (_eventQueue.Count == 0) return false;
            nextEvent = _eventQueue[0];
            _eventQueue.RemoveAt(0);
            return true;
        }
    }

    public void Clear()
    {
        lock (_eventQueue)
        {
            _eventQueue.Clear();
        }
    }
}

public class AudioVoice
{
    private const float SQRT_2 = 1.41421356237f;
    private const float SQRT_2_OVER_2 = SQRT_2 * 0.5f;

    public int slot;

    public SampleBuffer? sampleData;
    public bool reverb = false;
    public bool enabled = false;
    public int samplerate = 44100;
    public bool loopEnabled = false;
    public int loopStart = 0;
    public int loopEnd = 0;
    public float volume = 1f;
    public float pitch = 1f;
    public float detune = 0f;
    public float pan = 0f;
    public float fadeInLength = 0f;
    public float fadeOutLength = 0f;

    private double samplePos = 0.0;
    private float fade = 0.0f;
    private AudioVoiceEnvelopeState envelopeState = AudioVoiceEnvelopeState.Attack;

    public int stopPoint;
    public int playTime;

    public void Start()
    {
        if (enabled)
        {
            Console.WriteLine($"Voice {slot} already playing (state: {envelopeState}, envelope: {fade})");
        }

        if (fadeInLength <= 0f)
        {
            fade = 1.0f;
            envelopeState = AudioVoiceEnvelopeState.Sustain;
        }
        else
        {
            fade = 0.0f;
            envelopeState = AudioVoiceEnvelopeState.Attack;
        }
        samplePos = 0.0;
        enabled = true;
        playTime = 0;
    }

    public void Stop()
    {
        if (fadeOutLength <= 0f)
        {
            enabled = false;
        }
        else
        {
            envelopeState = AudioVoiceEnvelopeState.Release;
        }
    }

    public unsafe void Mix(int targetSamplerate, Span<float> destBuffer)
    {
        if (sampleData == null || !enabled) return;

        int endPos = sampleData.data.Count;

        if (loopEnabled && loopEnd > 0)
        {
            endPos = loopEnd;
        }

        // calculate speed at which to advance sample pos based on:
        // ratio between voice samplerate and target samplerate
        // pitch multiplier
        // detune offset in cents
        double sampleStep = (double)samplerate / targetSamplerate;
        sampleStep *= pitch * Math.Pow(2.0, detune / 1200.0f);

        // constant power panning
        float angle = pan * MathF.PI * 0.25f;
        float gainL = volume * SQRT_2_OVER_2 * (MathF.Cos(angle) - MathF.Sin(angle));
        float gainR = volume * SQRT_2_OVER_2 * (MathF.Cos(angle) + MathF.Sin(angle));

        float fadeInDelta = 1f / (fadeInLength * targetSamplerate);
        float fadeOutDelta = 1f / (fadeOutLength * targetSamplerate);

        for (int i = 0; i < destBuffer.Length; i += 2)
        {
            // update envelope
            switch (envelopeState)
            {
                case AudioVoiceEnvelopeState.Attack:
                    fade += fadeInDelta;
                    if (fade >= 1f)
                    {
                        fade = 1f;
                        envelopeState = AudioVoiceEnvelopeState.Sustain;
                    }
                    break;
                case AudioVoiceEnvelopeState.Release:
                    fade -= fadeOutDelta;
                    if (fade <= 0f)
                    {
                        fade = 0f;
                        enabled = false;
                        stopPoint = i;
                        return;
                    }
                    break;
            }

            int idx0 = (int)samplePos;
            int idx1 = idx0 + 1;
            if (idx1 >= sampleData.data.Count) idx1 = loopEnabled ? loopStart : idx0;

            float ratio = (float)(samplePos - idx0);

            // linear 2-tap sampling
            float s0 = sampleData.data[idx0];
            float s1 = sampleData.data[idx1];
            float s = s0 + ((s1 - s0) * ratio);

            destBuffer[i] += s * gainL * fade;
            destBuffer[i + 1] += s * gainR * fade;

            samplePos += sampleStep;
            playTime++;

            // if we hit end of sample, either return to loop point or just stop playing this voice depending on loop mode
            if (samplePos >= endPos)
            {
                if (loopEnabled)
                {
                    samplePos = loopStart;
                }
                else
                {
                    enabled = false;
                    stopPoint = i;
                    return;
                }
            }
        }
    }
}

public class SampleBuffer
{
    public List<float> data = [];
    public int refCount = 0;
}

public class AudioSystem : IDisposable
{
    private struct SampleData
    {
        public SampleBuffer pcmData;
        public int srcMemorySize;
    }

    private static readonly int[] ima_index_table = new int[] {
        -1, -1, -1, -1, 2, 4, 6, 8,
        -1, -1, -1, -1, 2, 4, 6, 8
    };

    private static readonly int[] ima_step_table = new int[] {
        7, 8, 9, 10, 11, 12, 13, 14, 16, 17,
        19, 21, 23, 25, 28, 31, 34, 37, 41, 45,
        50, 55, 60, 66, 73, 80, 88, 97, 107, 118,
        130, 143, 157, 173, 190, 209, 230, 253, 279, 307,
        337, 371, 408, 449, 494, 544, 598, 658, 724, 796,
        876, 963, 1060, 1166, 1282, 1411, 1552, 1707, 1878, 2066,
        2272, 2499, 2749, 3024, 3327, 3660, 4026, 4428, 4871, 5358,
        5894, 6484, 7132, 7845, 8630, 9493, 10442, 11487, 12635, 13899,
        15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794, 32767
    };

    // max 2MiB sample storage
    public const int MAX_AUDIO_MEM = 2097152;

    public const int CHANNELS = 2;
    public const int SAMPLERATE = 48000;

    public int TotalMemUsage => _totalAudioMem;
    public double AudioTime => _audioTime;

    public float volume = 1.0f;

    private AudioVoice[] _voices = new AudioVoice[32];
    private Freeverb _reverb;

    private AudioParamEventQueue _eventQueue = new AudioParamEventQueue();
    private double _audioTime = 0.0;

    private List<SampleData?> _audioSampleData = new List<SampleData?>();
    private int _totalAudioMem = 0;

    private float[] _reverbBuffer;
    private float[] _midiBuffer;

    private Synthesizer? _midiSynth;
    private MidiFileSequencer? _activeMidiSequencer;
    private bool _midiReverb = false;
    private float _midiVolume = 1f;
    private ulong _totalWritten = 0;
    private double _lastEvtTime = 0;

    private GCHandle _handle;
    private nint _audioStream;

    private Queue<SampleBuffer> _sampleBufferPool = new Queue<SampleBuffer>();
    private List<SampleBuffer> _usedBuffers = new List<SampleBuffer>();

    public AudioSystem()
    {
        _handle = GCHandle.Alloc(this);

        volume = 0.5f;

        for (int i = 0; i < _voices.Length; i++)
        {
            _voices[i] = new AudioVoice();
            _voices[i].slot = i;
        }

        _reverb = new Freeverb();
        _reverbBuffer = new float[2048];
        _midiBuffer = new float[2048];
        SetReverbParams();

        // open audio stream
        var audioSpec = new SDL.SDL_AudioSpec() {
            format = SDL.SDL_AudioFormat.SDL_AUDIO_F32,
            channels = CHANNELS,
            freq = SAMPLERATE,
        };
        _audioStream = SDL.SDL_OpenAudioDeviceStream(0xFFFFFFFF, ref audioSpec, HandleMixAudio, GCHandle.ToIntPtr(_handle));
        SDL.SDL_ResumeAudioStreamDevice(_audioStream);
        
        if (_audioStream == 0)
        {
            Console.WriteLine("Failed to create audio stream");
            return;
        }
    }

    public void Dispose()
    {
        SDL.SDL_PauseAudioStreamDevice(_audioStream);
        SDL.SDL_DestroyAudioStream(_audioStream);
        _handle.Free();
    }

    public void DumpSamples()
    {
        for (int i = 0; i < _audioSampleData.Count; i++)
        {
            if (_audioSampleData[i] is SampleData sampledata)
            {
                WaveFormat fmt = WaveFormat.CreateIeeeFloatWaveFormat(44100, 1);
                using (WaveFileWriter writer = new WaveFileWriter("_dump_sample_" + i + ".wav", fmt))
                {
                    writer.WriteSamples(sampledata.pcmData.data.ToArray(), 0, sampledata.pcmData.data.Count);
                }
            }
        }
    }

    public void SetReverbParams(float roomSize = 0.5f, float damp = 0.5f, float width = 1f, float wet = 0.2f, float dry = 0.8f)
    {
        _reverb.RoomSize = roomSize;
        _reverb.Damp = damp;
        _reverb.Width = width;
        _reverb.Wet = wet;
        _reverb.Dry = dry;
    }

    public bool InitSynth(byte[] sf2Data)
    {
        using (var memStream = new MemoryStream(sf2Data))
        {
            SoundFont soundFont = new SoundFont(memStream);
            SynthesizerSettings settings = new SynthesizerSettings(SAMPLERATE);
            settings.MaximumPolyphony = 24;
            settings.EnableReverbAndChorus = false;
            _midiSynth = new Synthesizer(soundFont, settings);
        }

        Console.WriteLine("Synth engine initialized");

        return true;
    }

    public bool PlayMidi(byte[] midiData, bool loop)
    {
        if (_midiSynth == null)
        {
            Console.WriteLine("Tried to play MIDI file without initializing synthesizer first");
            return false;
        }

        _activeMidiSequencer?.Stop();
        _activeMidiSequencer = null;

        using (var memStream = new MemoryStream(midiData))
        {
            MidiFile midiFile = new MidiFile(memStream, MidiFileLoopType.RpgMaker);
            _activeMidiSequencer = new MidiFileSequencer(_midiSynth);
            _activeMidiSequencer.Play(midiFile, loop);
        }

        return true;
    }

    public void SetMidiVolume(float volume)
    {
        _midiVolume = volume;
    }

    public void SetMidiReverb(bool enabled)
    {
        _midiReverb = enabled;
    }

    public void Reset()
    {
        foreach (var voice in _voices)
        {
            voice.Stop();
        }

        for (int i = 0; i < _audioSampleData.Count; i++)
        {
            _audioSampleData[i] = null;
        }

        _midiSynth = null;
        _activeMidiSequencer?.Stop();
        _activeMidiSequencer = null;

        _totalAudioMem = 0;
        _eventQueue.Clear();
    }

    public bool GetVoiceState(int slot)
    {
        if (slot < 0 || slot >= _voices.Length)
        {
            Console.WriteLine("Tried to get state if invalid voice slot");
            return false;
        }

        return _voices[slot].enabled;
    }

    public unsafe int AllocateCompressed(Span<byte> data, int chunkLength)
    {
        if (_totalAudioMem + data.Length > MAX_AUDIO_MEM)
        {
            Console.WriteLine("Exceeded 2MB audio storage limit");
            return -1;
        }

        // each chunk starts with a 4-byte preamble, and the remaining bytes each encode 2 samples
        int blockCount = data.Length / chunkLength;
        int bytesPerChunk = chunkLength - 4;
        int samplesPerChunk = bytesPerChunk * 2;
        
        SampleBuffer pcm_f32 = AllocSampleBuffer();
        pcm_f32.refCount = 1;
        pcm_f32.data.Clear();
        pcm_f32.data.EnsureCapacity(blockCount * samplesPerChunk);

        _usedBuffers.Add(pcm_f32);

        for (int i = 0; i < (blockCount * samplesPerChunk); i++) {
            pcm_f32.data.Add(0);
        }

        for (int i = 0; i < blockCount; i++)
        {
            Span<byte> block = data.Slice(i * chunkLength, chunkLength);
            ushort initPredictor = (ushort)(block[0] | (block[1] << 8));
            byte initStepIndex = block[2];
            DecodeADPCM((short)initPredictor, initStepIndex, block.Slice(4), CollectionsMarshal.AsSpan(pcm_f32.data).Slice(i * samplesPerChunk, samplesPerChunk));
        }

        _totalAudioMem += data.Length;

        int targetSlot = -1;

        for (int i = 0; i < _audioSampleData.Count; i++)
        {
            if (_audioSampleData[i] == null)
            {
                _audioSampleData[i] = new SampleData { pcmData = pcm_f32, srcMemorySize = data.Length };
                targetSlot = i;
            }
        }

        if (targetSlot == -1)
        {
            targetSlot = _audioSampleData.Count;
            _audioSampleData.Add(new SampleData { pcmData = pcm_f32, srcMemorySize = data.Length });
        }

        return targetSlot;
    }

    public unsafe int Allocate(Span<byte> data, AudioFormat format)
    {
        if (_totalAudioMem + data.Length > MAX_AUDIO_MEM)
        {
            Console.WriteLine("Exceeded 2MB audio storage limit");
            return -1;
        }

        SampleBuffer pcm_f32 = AllocSampleBuffer();
        pcm_f32.refCount = 1;
        pcm_f32.data.Clear();

        _usedBuffers.Add(pcm_f32);

        switch (format)
        {
            case AudioFormat.PCM_S8:
                {
                    pcm_f32.data.EnsureCapacity(data.Length);

                    for (int i = 0; i < data.Length; i++)
                    {
                        pcm_f32.data.Add(((sbyte)data[i]) / 128.0f);
                    }
                    break;
                }
            case AudioFormat.PCM_S16:
                {
                    pcm_f32.data.EnsureCapacity(data.Length / 2);

                    fixed (void* srcptr = data)
                    {
                        short* srcptr_s = (short*)srcptr;
                        int len = data.Length / 2;
                        for (int i = 0; i < len; i++)
                        {
                            pcm_f32.data.Add(srcptr_s[i] / 32768.0f);
                        }
                    }
                    break;
                }
            default:
                Console.WriteLine("Invalid audio format specified");
                return -1;
        }

        _totalAudioMem += data.Length;

        int targetSlot = -1;

        for (int i = 0; i < _audioSampleData.Count; i++)
        {
            if (_audioSampleData[i] == null)
            {
                _audioSampleData[i] = new SampleData { pcmData = pcm_f32, srcMemorySize = data.Length };
                targetSlot = i;
                break;
            }
        }

        if (targetSlot == -1)
        {
            targetSlot = _audioSampleData.Count;
            _audioSampleData.Add(new SampleData { pcmData = pcm_f32, srcMemorySize = data.Length });
        }

        return targetSlot;
    }

    public void Free(int handle)
    {
        if (handle < 0 || handle >= _audioSampleData.Count)
        {
            Console.WriteLine("Tried to free invalid audio sample handle");
            return;
        }

        if (_audioSampleData[handle]?.pcmData is SampleBuffer sampleBuffer)
        {
            sampleBuffer.refCount -= 1;
        }

        _totalAudioMem -= _audioSampleData[handle]?.srcMemorySize ?? 0;
        _audioSampleData[handle] = null;
    }

    public void QueueStartVoice(int slot, double time)
    {
        _eventQueue.Insert(new AudioParamEvent
        {
            target = _voices[slot],
            parameter = AudioVoiceParameter.Start,
            time = time
        });
    }

    public void QueueStopVoice(int slot, double time)
    {
        _eventQueue.Insert(new AudioParamEvent
        {
            target = _voices[slot],
            parameter = AudioVoiceParameter.Stop,
            time = time
        });
    }

    public void QueueSetVoiceParam_i(int slot, AudioVoiceParameter param, int value, double time)
    {
        _eventQueue.Insert(new AudioParamEvent
        {
            target = _voices[slot],
            parameter = param,
            value = new AudioParamEventValue { ivalue = value },
            time = time
        });
    }

    public void QueueSetVoiceParam_f(int slot, AudioVoiceParameter param, float value, double time)
    {
        _eventQueue.Insert(new AudioParamEvent
        {
            target = _voices[slot],
            parameter = param,
            value = new AudioParamEventValue { fvalue = value },
            time = time
        });
    }

    private SampleBuffer AllocSampleBuffer()
    {
        if (_sampleBufferPool.Count > 0) {
            return _sampleBufferPool.Dequeue();
        }

        return new SampleBuffer();
    }

    private void ReturnSampleBuffer(SampleBuffer buffer)
    {
        _sampleBufferPool.Enqueue(buffer);
    }

    private void DecodeADPCM(short initPredictor, byte initStepIndex, Span<byte> src, Span<float> dst)
    {
        int predictor = (int)initPredictor;
        int step_index = (int)initStepIndex;
        int step = ima_step_table[step_index];

        int outidx = 0;

        for (int i = 0; i < src.Length; i++)
        {
            int n0 = src[i] & 0xF;
            int n1 = (src[i] >> 4) & 0xF;

            // decode low nibble

            step_index = step_index + ima_index_table[n0];
            if (step_index < 0) step_index = 0;
            if (step_index > 88) step_index = 88;

            int sign = n0 & 8;
            int delta = n0 & 7;
            int diff = step >> 3;
            if ((delta & 4) != 0) diff += step;
            if ((delta & 2) != 0) diff += (step >> 1);
            if ((delta & 1) != 0) diff += (step >> 2);
            if (sign != 0) predictor -= diff;
            else predictor += diff;

            step = ima_step_table[step_index];

            if (predictor < short.MinValue) predictor = short.MinValue;
            if (predictor > short.MaxValue) predictor = short.MaxValue;

            dst[outidx++] = predictor / 32768.0f;

            // decode high nibble

            step_index = step_index + ima_index_table[n1];
            if (step_index < 0) step_index = 0;
            if (step_index > 88) step_index = 88;

            sign = n1 & 8;
            delta = n1 & 7;
            diff = step >> 3;
            if ((delta & 4) != 0) diff += step;
            if ((delta & 2) != 0) diff += (step >> 1);
            if ((delta & 1) != 0) diff += (step >> 2);
            if (sign != 0) predictor -= diff;
            else predictor += diff;

            step = ima_step_table[step_index];

            if (predictor < short.MinValue) predictor = short.MinValue;
            if (predictor > short.MaxValue) predictor = short.MaxValue;

            dst[outidx++] = predictor / 32768.0f;
        }
    }

    public unsafe void FillBuffer(Span<float> buffer)
    {
        double audioBufferDuration = (buffer.Length / CHANNELS) / (double)SAMPLERATE;

        if (_reverbBuffer.Length < buffer.Length) {
            _reverbBuffer = new float[buffer.Length];
        }

        if (_midiBuffer.Length < buffer.Length) {
            _midiBuffer = new float[buffer.Length];
        }

        // clear buffers
        for (int i = 0; i < buffer.Length; i++)
        {
            buffer[i] = 0f;
            _reverbBuffer[i] = 0f;
            _midiBuffer[i] = 0f;
        }

        int totalWrite = buffer.Length / CHANNELS;

        // render midi into either reverb input buffer or to output
        if (_midiSynth != null)
        {
            _midiSynth.MasterVolume = _midiVolume;
        }

        if (_midiReverb)
        {
            _activeMidiSequencer?.RenderInterleaved(_reverbBuffer.AsSpan()[..buffer.Length]);
        }
        else
        {
            _activeMidiSequencer?.RenderInterleaved(_midiBuffer.AsSpan()[..buffer.Length]);

            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] = _midiBuffer[i];
            }
        }

        // mix voices into either reverb input buffer or output buffer
        int written = 0;
        while (written < totalWrite)
        {
            if (_eventQueue.TryPeek(out var nextEvent) && nextEvent.time < (_audioTime + audioBufferDuration))
            {
                _eventQueue.Dequeue();

                double evDelta = nextEvent.time - _lastEvtTime;
                _lastEvtTime = nextEvent.time;

                // mix up to this point, then process event before resuming
                int mixLen = (int)((nextEvent.time - _audioTime) * SAMPLERATE) - written;

                if (mixLen > 0)
                {
                    int bufferStart = written * CHANNELS;
                    int bufferEnd = bufferStart + (mixLen * CHANNELS);
                    var reverbSlice = _reverbBuffer.AsSpan()[bufferStart..bufferEnd];
                    var drySlice = buffer[bufferStart..bufferEnd];
                    MixVoices(reverbSlice, drySlice);
                    written += mixLen;
                }

                switch (nextEvent.parameter)
                {
                    case AudioVoiceParameter.SampleData:
                        if (nextEvent.value.ivalue < 0 || nextEvent.value.ivalue >= _audioSampleData.Count)
                        {
                            Console.WriteLine("Tried to assign invalid sample handle to voice: " + nextEvent.value.ivalue);
                        }
                        else
                        {
                            if (nextEvent.target.sampleData is SampleBuffer prevBuff)
                            {
                                prevBuff.refCount--;
                            }

                            nextEvent.target.sampleData = _audioSampleData[nextEvent.value.ivalue]?.pcmData;
                            
                            if (nextEvent.target.sampleData is SampleBuffer nextBuff)
                            {
                                nextBuff.refCount++;
                            }
                        }
                        break;
                    case AudioVoiceParameter.Samplerate:
                        nextEvent.target.samplerate = nextEvent.value.ivalue;
                        break;
                    case AudioVoiceParameter.LoopEnabled:
                        nextEvent.target.loopEnabled = nextEvent.value.ivalue != 0;
                        break;
                    case AudioVoiceParameter.LoopStart:
                        nextEvent.target.loopStart = nextEvent.value.ivalue;
                        break;
                    case AudioVoiceParameter.LoopEnd:
                        nextEvent.target.loopEnd = nextEvent.value.ivalue;
                        break;
                    case AudioVoiceParameter.Volume:
                        nextEvent.target.volume = nextEvent.value.fvalue;
                        break;
                    case AudioVoiceParameter.Pitch:
                        nextEvent.target.pitch = nextEvent.value.fvalue;
                        break;
                    case AudioVoiceParameter.Detune:
                        nextEvent.target.detune = nextEvent.value.fvalue;
                        break;
                    case AudioVoiceParameter.Pan:
                        nextEvent.target.pan = nextEvent.value.fvalue;
                        break;
                    case AudioVoiceParameter.Reverb:
                        nextEvent.target.reverb = nextEvent.value.ivalue != 0;
                        break;
                    case AudioVoiceParameter.FadeInDuration:
                        nextEvent.target.fadeInLength = nextEvent.value.fvalue;
                        break;
                    case AudioVoiceParameter.FadeOutDuration:
                        nextEvent.target.fadeOutLength = nextEvent.value.fvalue;
                        break;
                    case AudioVoiceParameter.Start:
                        nextEvent.target.Start();
                        break;
                    case AudioVoiceParameter.Stop:
                        nextEvent.target.Stop();
                        break;
                }
            }
            else
            {
                // just mix remainder of buffer
                int mixLen = (buffer.Length / CHANNELS) - written;
                int bufferStart = written * CHANNELS;
                int bufferEnd = bufferStart + (mixLen * CHANNELS);
                var reverbSlice = _reverbBuffer.AsSpan()[bufferStart..bufferEnd];
                var drySlice = buffer[bufferStart..bufferEnd];
                MixVoices(reverbSlice, drySlice);
                written += mixLen;
            }
        }

        // mix reverb into output
        unsafe {
            fixed (float* inputL = &_reverbBuffer[0])
            fixed (float* inputR = &_reverbBuffer[1])
            fixed (float* outputL = &buffer[0])
            fixed (float* outputR = &buffer[1])
            {
                _reverb.ProcessMix(inputL, inputR, outputL, outputR, totalWrite, 2);
            }
        }

        // scale
        for (int i = 0; i < buffer.Length; i++)
        {
            buffer[i] *= volume;
        }

        _audioTime += audioBufferDuration;

        // return any sample buffers which are no longer referenced to the pool
        for (int i = _usedBuffers.Count - 1; i >= 0; i--)
        {
            if (_usedBuffers[i].refCount == 0)
            {
                ReturnSampleBuffer(_usedBuffers[i]);
                _usedBuffers.RemoveAt(i);
            }
        }
    }

    private unsafe void MixVoices(Span<float> targetReverbBuffer, Span<float> targetDryBuffer)
    {
        for (int i = 0; i < _voices.Length; i++)
        {
            _voices[i].Mix(SAMPLERATE, _voices[i].reverb ? targetReverbBuffer : targetDryBuffer);
        }
        _totalWritten += (ulong)(targetReverbBuffer.Length / 2);
    }

    static float[] _audioBuf = new float[2048];
    static unsafe void HandleMixAudio(nint userdata, nint stream, int additional_amount, int total_amount)
    {
        if (additional_amount == 0) return;

        if (_audioBuf.Length < total_amount)
        {
            _audioBuf = new float[total_amount];
        }

        var buf = _audioBuf.AsSpan()[..additional_amount];

        if (GCHandle.FromIntPtr(userdata).Target is AudioSystem instance)
        {
            instance.FillBuffer(buf);
        }

        fixed (float* ptr = buf) {
            SDL.SDL_PutAudioStreamData(stream, (nint)ptr, buf.Length * 4);
        }
    }
}