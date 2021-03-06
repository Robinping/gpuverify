//===-----------------------------------------------------------------------==//
//
//                GPUVerify - a Verifier for GPU Kernels
//
// This file is distributed under the Microsoft Public License.  See
// LICENSE.TXT for details.
//
//===----------------------------------------------------------------------===//

using System.Collections.Generic;
using Microsoft.Boogie;

namespace GPUVerify
{
    class NullRaceInstrumenter : IRaceInstrumenter
    {

        public void AddRaceCheckingCandidateInvariants(Implementation impl, IRegion region)
        {

        }

        public void AddKernelPrecondition()
        {

        }

        public void AddRaceCheckingInstrumentation()
        {

        }

        public Microsoft.Boogie.BigBlock MakeResetReadWriteSetStatements(Variable v, Expr ResetCondition)
        {
            return new BigBlock(Token.NoToken, null, new List<Cmd>(), null, null);
        }

        public void AddRaceCheckingCandidateRequires(Procedure Proc)
        {

        }

        public void AddRaceCheckingCandidateEnsures(Procedure Proc)
        {

        }

        public void AddRaceCheckingDeclarations()
        {

        }

        public void AddSourceLocationLoopInvariants(Implementation impl, IRegion region)
        {

        }

        public void AddStandardSourceVariablePreconditions()
        {

        }

        public void AddStandardSourceVariablePostconditions()
        {

        }

        public void AddDefaultLoopInvariants()
        {

        }

        public void AddDefaultContracts()
        {

        }

    }
}
