using PrivatePrep.Models;

namespace PrivatePrep.Services;

public interface ISpeechService
{
    Task<SpeechResult> SynthesizeAsync(SpeechRequest request, CancellationToken cancellationToken = default);
}
