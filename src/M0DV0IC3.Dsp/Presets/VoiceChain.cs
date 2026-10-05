using M0DV0IC3.Dsp.Dynamics;
using M0DV0IC3.Dsp.Effects;
using M0DV0IC3.Dsp.Filters;
using M0DV0IC3.Dsp.Pitch;

namespace M0DV0IC3.Dsp.Presets;

/// <summary>
/// La cadena completa de una voz, con estructura fija:
/// invertir → PSOLA (tono, formantes, robot, autotune y vibrato) o vibrato por retardo → coro → susurro → aire →
/// vocoder → ring mod → comb → distorsión → bitcrusher → filtros → compresor → transmisión → chorus → flanger →
/// eco → reverb → ganancia.
/// Los efectos apagados no consumen CPU.
/// Se construye en el hilo de la UI, porque ahí se reserva toda la memoria. Después se le pueden mandar
/// parámetros nuevos (<see cref="TryUpdate"/>) que el hilo de audio aplica sin reservar nada al principio
/// del siguiente bloque.
/// </summary>
public sealed class VoiceChain : IAudioEffect
{
    private readonly int _sampleRate;
    private readonly Reverse? _reverse;
    private readonly PsolaPitchShifter? _psola;
    private readonly Harmonizer? _harmonizer;
    private readonly Vibrato _vibrato;
    private readonly Whisper _whisper;
    private readonly Breath _breath;
    private readonly Vocoder _vocoder;
    private readonly RingModulator _ring;
    private readonly CombFilter _comb;
    private readonly Distortion _distortion = new();
    private readonly Bitcrusher _crusher;
    private readonly Biquad _highPass = new();
    private readonly Biquad _lowPass = new();
    private readonly Biquad _presence = new();
    private readonly Biquad _highShelf = new();
    private readonly Biquad _lowShelf = new();
    private readonly Biquad _nasal = new();
    private readonly Compressor _compressor;
    private readonly Transmission _transmission;
    private readonly Chorus _chorus;
    private readonly Flanger _flanger;
    private readonly Echo _echo;
    private readonly Reverb _reverb;
    private float _outputGain = 1f;
    private VoicePreset? _pending;

    /// <param name="profile">Tono medio de la voz, compartido entre cadenas (para las voces con tono objetivo).</param>
    public VoiceChain(VoicePreset preset, int sampleRate = DspMath.SampleRate, VoiceRange range = VoiceRange.Medium, PitchProfile? profile = null)
    {
        _sampleRate = sampleRate;
        Range = range;
        if (preset.UsesReverse) _reverse = new Reverse(sampleRate);
        if (preset.UsesPitch) _psola = new PsolaPitchShifter(sampleRate, range, profile);
        if (preset.UsesHarmony) _harmonizer = new Harmonizer(sampleRate, range);
        _vibrato = new Vibrato(sampleRate);
        _whisper = new Whisper(sampleRate);
        _breath = new Breath(sampleRate);
        _vocoder = new Vocoder(sampleRate);
        _compressor = new Compressor(sampleRate);
        _transmission = new Transmission(sampleRate);
        _ring = new RingModulator(sampleRate);
        _comb = new CombFilter(sampleRate);
        _crusher = new Bitcrusher(sampleRate);
        _chorus = new Chorus(sampleRate);
        _flanger = new Flanger(sampleRate);
        _echo = new Echo(sampleRate);
        _reverb = new Reverb(sampleRate);
        Preset = preset;
        Apply(preset);
    }

    /// <summary>Último preset enviado (puede que el hilo de audio aún no lo haya aplicado).</summary>
    public VoicePreset Preset { get; private set; }

    public VoiceRange Range { get; }

    public bool UsesPitch => _psola is not null;

    public PsolaPitchShifter? PitchShifter => _psola;

    public int LatencySamples => (_reverse?.LatencySamples ?? 0) + (_psola?.LatencySamples ?? _vibrato.LatencySamples);

    /// <summary>
    /// Pide cambiar los parámetros en caliente; los aplica el hilo de audio. Devuelve false si el preset
    /// necesita otra estructura (activar o quitar PSOLA o la voz invertida): en ese caso hay que crear otra cadena.
    /// </summary>
    public bool TryUpdate(VoicePreset preset)
    {
        if (preset.UsesPitch != UsesPitch || preset.UsesReverse != (_reverse is not null)
            || preset.UsesHarmony != (_harmonizer is not null)) return false;
        Preset = preset;
        Volatile.Write(ref _pending, preset);
        return true;
    }

    public void Process(Span<float> buffer)
    {
        var pending = Interlocked.Exchange(ref _pending, null);
        if (pending is not null) Apply(pending);

        _reverse?.Process(buffer);
        if (_psola is not null) _psola.Process(buffer);
        else _vibrato.Process(buffer);
        _harmonizer?.Process(buffer);
        _whisper.Process(buffer);
        _breath.Process(buffer);
        _vocoder.Process(buffer);
        _ring.Process(buffer);
        _comb.Process(buffer);
        _distortion.Process(buffer);
        _crusher.Process(buffer);
        _highPass.Process(buffer);
        _lowPass.Process(buffer);
        _presence.Process(buffer);
        _highShelf.Process(buffer);
        _lowShelf.Process(buffer);
        _nasal.Process(buffer);
        _compressor.Process(buffer);
        _transmission.Process(buffer);
        _chorus.Process(buffer);
        _flanger.Process(buffer);
        _echo.Process(buffer);
        _reverb.Process(buffer);

        if (_outputGain != 1f)
        {
            for (int i = 0; i < buffer.Length; i++) buffer[i] *= _outputGain;
        }
    }

    public void Reset()
    {
        _reverse?.Reset();
        _psola?.Reset();
        _harmonizer?.Reset();
        _vibrato.Reset();
        _whisper.Reset();
        _breath.Reset();
        _vocoder.Reset();
        _ring.Reset();
        _comb.Reset();
        _crusher.Reset();
        _highPass.Reset();
        _lowPass.Reset();
        _presence.Reset();
        _highShelf.Reset();
        _lowShelf.Reset();
        _nasal.Reset();
        _compressor.Reset();
        _transmission.Reset();
        _chorus.Reset();
        _flanger.Reset();
        _echo.Reset();
        _reverb.Reset();
    }

    private void Apply(VoicePreset p)
    {
        _reverse?.Configure(p.ReverseMs);
        if (_psola is not null)
        {
            // Con PSOLA, el vibrato se hace en los granos: sin latencia extra.
            _psola.SetParameters(DspMath.SemitonesToRatio(p.PitchSemitones), p.FormantRatio, p.RobotHz);
            _psola.SetAutotune(p.AutotuneScale, p.AutotuneKey, p.AutotuneRetuneMs, p.AutotuneExaggeration);
            _psola.SetVibrato(p.VibratoHz, p.VibratoSemitones);
            _psola.SetTargetPitch(p.TargetPitchHz);
        }
        else
        {
            _vibrato.Configure(p.VibratoHz, p.VibratoSemitones);
        }
        _whisper.Configure(p.WhisperMix);
        _breath.Configure(p.Breathiness);
        _harmonizer?.Configure(p.HarmonyMix);
        _vocoder.Configure(p.VocoderMix, p.VocoderHz);
        _ring.Configure(p.RingModHz, p.RingModMix);
        _comb.Configure(p.CombMs, p.CombFeedback, p.CombMix);
        _distortion.Configure(p.Distortion);
        _crusher.Configure(p.BitcrushRateHz, p.BitcrushBits);

        if (p.HighPassHz > 0) _highPass.SetHighPass(_sampleRate, p.HighPassHz); else _highPass.SetBypass();
        if (p.LowPassHz > 0) _lowPass.SetLowPass(_sampleRate, p.LowPassHz); else _lowPass.SetBypass();
        if (Math.Abs(p.PresenceDb) > 0.05) _presence.SetPeaking(_sampleRate, 1800, 1.0, p.PresenceDb); else _presence.SetBypass();
        if (Math.Abs(p.HighShelfDb) > 0.05) _highShelf.SetHighShelf(_sampleRate, 3500, p.HighShelfDb); else _highShelf.SetBypass();
        if (Math.Abs(p.LowShelfDb) > 0.05) _lowShelf.SetLowShelf(_sampleRate, 150, p.LowShelfDb); else _lowShelf.SetBypass();
        if (Math.Abs(p.NasalDb) > 0.05) _nasal.SetPeaking(_sampleRate, 1100, 2.5, p.NasalDb); else _nasal.SetBypass();
        _compressor.Configure(p.Compression);
        _transmission.Configure(p.Transmission, p.TransmissionNoise);

        _chorus.Configure(p.ChorusMix);
        _flanger.Configure(p.FlangerMix, p.FlangerHz);
        _echo.Configure(p.EchoMs, p.EchoFeedback, p.EchoMix);
        _reverb.Configure(p.ReverbMix, p.ReverbSize);
        _outputGain = DspMath.DbToGain(p.OutputGainDb);
    }
}
