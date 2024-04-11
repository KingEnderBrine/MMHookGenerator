using Microsoft.VisualStudio.Language.Intellisense;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MMHookGenerator.Intellisense
{
    public class HookCompletionSource : ICompletionSource
    {
        public void AugmentCompletionSession(ICompletionSession session, IList<CompletionSet> completionSets)
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            throw new NotImplementedException();
        }
    }
}
