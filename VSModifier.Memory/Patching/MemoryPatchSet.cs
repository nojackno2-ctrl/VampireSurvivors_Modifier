namespace VSModifier.Memory.Patching;

public sealed class MemoryPatchSet(IReadOnlyList<MemoryPatch> patches) : IDisposable
{
    private bool _enabled;

    public void Enable()
    {
        if (_enabled)
        {
            return;
        }

        int enabledCount = 0;
        try
        {
            foreach (MemoryPatch patch in patches)
            {
                patch.Enable();
                enabledCount++;
            }

            _enabled = true;
        }
        catch (Exception enableError)
        {
            Exception? restorationError = null;
            for (int index = enabledCount - 1; index >= 0; index--)
            {
                try
                {
                    patches[index].Disable();
                }
                catch (Exception exception)
                {
                    restorationError ??= exception;
                }
            }

            if (restorationError is not null)
            {
                throw new InvalidOperationException(
                    "複合 patch 套用失敗，且至少一段原始位元組無法還原。",
                    new AggregateException(enableError, restorationError));
            }

            throw;
        }
    }

    public void Disable()
    {
        if (!_enabled)
        {
            return;
        }

        Exception? firstError = null;
        for (int index = patches.Count - 1; index >= 0; index--)
        {
            try
            {
                patches[index].Disable();
            }
            catch (Exception exception)
            {
                firstError ??= exception;
            }
        }

        _enabled = false;
        if (firstError is not null)
        {
            throw new InvalidOperationException("至少一段記憶體 patch 還原失敗。", firstError);
        }
    }

    public void Dispose() => Disable();
}
