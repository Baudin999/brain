using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace Brain.Search;

/// <summary>
/// The model of an <see cref="EmbedderProfile"/>, run in-process by ONNX Runtime on the CPU: a text's vector is the
/// output the profile pools, normalized. brain install downloads the profile's files into the model's directory under
/// the tool's cache/model. Two intra-op threads that never spin keep a full re-embed light on the machine. The model loads
/// on the first call; a missing or broken model answers null, so lookup falls back to its other rankings.
/// </summary>
internal sealed class OnnxEmbedder(string modelRoot, EmbedderProfile profile) : IEmbedder, IDisposable
{
    private const int Threads = 2;
    private const string InputIds = "input_ids";
    private const string AttentionMask = "attention_mask";
    private const string TokenTypeIds = "token_type_ids";
    private const string SentenceEmbedding = "sentence_embedding";
    private const string NoSpinning = "session.intra_op.allow_spinning";
    private const string Off = "0";

    /// <summary>Loads once, even when the window's requests ask at the same time; null when the model did not load.</summary>
    private readonly Lazy<Loaded?> loaded = new(() => Load(profile.Directory(modelRoot), profile), LazyThreadSafetyMode.ExecutionAndPublication);

    public EmbedderProfile Profile => profile;

    public string Silence => $"the embedding model {profile.Name} did not load, and brain install downloads it";

    public IReadOnlyList<float[]>? Embed(IReadOnlyList<string> texts)
    {
        if (loaded.Value is not { } model)
        {
            return null;
        }

        try
        {
            return [.. texts.Select(model.Vector)];
        }
        catch (Exception error) when (error is OnnxRuntimeException or InvalidDataException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (loaded.IsValueCreated)
        {
            loaded.Value?.Dispose();
        }
    }

    private static Loaded? Load(string directory, EmbedderProfile profile)
    {
        try
        {
            return Loaded.From(directory, profile);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or OnnxRuntimeException
                                          or DllNotFoundException or TypeInitializationException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    /// <summary>A text's ids between its first and last token, the ids that open and close it, and the unpadded body.</summary>
    private sealed record Tokens(long First, long Last, Func<string, IEnumerable<long>> Body)
    {
        /// <summary>XLM-R's ids for &lt;s&gt;, &lt;/s&gt; and &lt;unk&gt;.</summary>
        private const long Begin = 0;
        private const long End = 2;
        private const long Unknown = 3;

        public static Tokens Read(TokenizerKind kind, string path)
        {
            if (kind == TokenizerKind.WordPiece)
            {
                var bert = BertTokenizer.Create(path, new BertOptions { LowerCaseBeforeTokenization = true });
                return new(bert.ClassificationTokenId, bert.SeparatorTokenId, text => bert.EncodeToIds(text, addSpecialTokens: false).Select(id => (long)id));
            }

            SentencePieceTokenizer pieces;
            using (var stream = File.OpenRead(path))
            {
                pieces = SentencePieceTokenizer.Create(stream, addBeginningOfSentence: false, addEndOfSentence: false);
            }

            // Every piece sits one higher in the model than in the SentencePiece model, and its unknown piece is <unk>.
            return new(Begin, End, text => pieces.EncodeToIds(text).Select(id => id == pieces.UnknownId ? Unknown : id + 1L));
        }
    }

    /// <summary>The ONNX session and the tokenizer, alive for as long as the process asks for vectors.</summary>
    private sealed class Loaded : IDisposable
    {
        private readonly EmbedderProfile profile;
        private readonly Tokens tokens;
        private readonly InferenceSession session;

        private Loaded(EmbedderProfile profile, string model, string tokenizer)
        {
            this.profile = profile;
            tokens = Tokens.Read(profile.Tokenizing, tokenizer);
            using var options = new SessionOptions
            {
                IntraOpNumThreads = Threads,
                InterOpNumThreads = 1,
                ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            };
            options.AddSessionConfigEntry(NoSpinning, Off);
            session = new InferenceSession(model, options);
        }

        public static Loaded From(string directory, EmbedderProfile profile)
        {
            var model = Path.Combine(directory, profile.Weights.Name);
            var tokenizer = Path.Combine(directory, profile.Tokenizer.Name);
            return File.Exists(model) && File.Exists(tokenizer) ? new Loaded(profile, model, tokenizer) : throw new FileNotFoundException(model);
        }

        /// <summary>The text's normalized vector. A model whose vector is not the profile's dimension is broken.</summary>
        public float[] Vector(string text)
        {
            long[] ids = [tokens.First, .. tokens.Body(text).Take(profile.MaxTokens - 2), tokens.Last];
            int[] shape = [1, ids.Length];
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(InputIds, new DenseTensor<long>(ids, shape)),
                NamedOnnxValue.CreateFromTensor(AttentionMask, new DenseTensor<long>(Filled(ids.Length, 1), shape)),
            };
            if (session.InputMetadata.ContainsKey(TokenTypeIds))
            {
                inputs.Add(NamedOnnxValue.CreateFromTensor(TokenTypeIds, new DenseTensor<long>(Filled(ids.Length, 0), shape)));
            }

            var vector = profile.Pooling == Pooling.SentenceEmbedding ? Pooled(inputs) : FirstToken(inputs);
            return vector.Length == profile.Dimension
                ? Normalized(vector)
                : throw new InvalidDataException($"{profile.Name} gave a vector of {vector.Length}, not {profile.Dimension}");
        }

        public void Dispose() => session.Dispose();

        private float[] Pooled(List<NamedOnnxValue> inputs)
        {
            using var outputs = session.Run(inputs, [SentenceEmbedding]);
            return [.. outputs[0].AsEnumerable<float>()];
        }

        private float[] FirstToken(List<NamedOnnxValue> inputs)
        {
            using var outputs = session.Run(inputs);
            var hidden = outputs[0].AsTensor<float>();
            var vector = new float[hidden.Dimensions[2]];
            for (var i = 0; i < vector.Length; i++)
            {
                vector[i] = hidden[0, 0, i];
            }

            return vector;
        }

        private static long[] Filled(int length, long value)
        {
            var values = new long[length];
            Array.Fill(values, value);
            return values;
        }

        private static float[] Normalized(float[] vector)
        {
            var norm = MathF.Sqrt(vector.Sum(value => value * value));
            return norm == 0 ? vector : [.. vector.Select(value => value / norm)];
        }
    }
}
