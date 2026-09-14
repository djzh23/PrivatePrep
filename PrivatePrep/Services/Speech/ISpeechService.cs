using PrivatePrep.Models;

namespace PrivatePrep.Services.Speech;

public interface ISpeechService
{
    Task<SpeechResult> SynthesizeAsync(SpeechRequest request, CancellationToken cancellationToken = default);
}
