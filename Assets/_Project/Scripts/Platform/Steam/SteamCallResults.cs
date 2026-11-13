using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Steamworks;

namespace Game.Platform.Steam
{
    internal static class SteamCallResults
    {
        /// <summary>Awaits a Steam async call (CallResult) as a UniTask.</summary>
        public static async UniTask<T> AwaitResult<T>(SteamAPICall_t call, CancellationToken cancellationToken) where T : struct
        {
            var completion = new UniTaskCompletionSource<T>();
            CallResult<T> callResult = CallResult<T>.Create((result, ioFailure) =>
            {
                if (ioFailure)
                {
                    completion.TrySetException(new InvalidOperationException($"Steam IO failure for {typeof(T).Name}."));
                }
                else
                {
                    completion.TrySetResult(result);
                }
            });

            callResult.Set(call);
            try
            {
                using (cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken)))
                {
                    return await completion.Task;
                }
            }
            finally
            {
                callResult.Cancel();
                callResult.Dispose();
            }
        }
    }
}
