using System;

namespace FFXIManager.Infrastructure
{
    public interface IUiDispatcher
    {
        bool CheckAccess();
        void Invoke(Action action);
        void BeginInvoke(Action action);
    }
}
