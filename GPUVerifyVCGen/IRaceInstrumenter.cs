//===-----------------------------------------------------------------------==//
//
//                GPUVerify - a Verifier for GPU Kernels
//
// This file is distributed under the Microsoft Public License.  See
// LICENSE.TXT for details.
//
//===----------------------------------------------------------------------===//

using Microsoft.Boogie;

namespace GPUVerify
{
    interface IRaceInstrumenter
    {
        void AddRaceCheckingCandidateInvariants(Implementation impl, IRegion region);

        void AddKernelPrecondition();

        void AddRaceCheckingInstrumentation();

        void AddRaceCheckingDeclarations();

        BigBlock MakeResetReadWriteSetStatements(Variable v, Expr ResetCondition);

        void AddRaceCheckingCandidateRequires(Procedure Proc);

        void AddRaceCheckingCandidateEnsures(Procedure Proc);

        void AddDefaultLoopInvariants();

        void AddDefaultContracts();

    }
}
