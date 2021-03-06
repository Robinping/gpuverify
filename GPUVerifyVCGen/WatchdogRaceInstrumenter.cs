//===-----------------------------------------------------------------------==//
//
//                GPUVerify - a Verifier for GPU Kernels
//
// This file is distributed under the Microsoft Public License.  See
// LICENSE.TXT for details.
//
//===----------------------------------------------------------------------===//

using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using Microsoft.Boogie;

namespace GPUVerify
{
  class WatchdogRaceInstrumenter : RaceInstrumenter
  {
    internal WatchdogRaceInstrumenter(GPUVerifier verifier) : base(verifier) {

    }

    protected override void AddLogAccessProcedure(Variable v, AccessType Access) {

      // This array should be included in the set of global or group shared arrays that
      // are *not* disabled
      Debug.Assert(verifier.KernelArrayInfo.ContainsGlobalOrGroupSharedArray(v, false));

      Procedure LogAccessProcedure = MakeLogAccessProcedureHeader(v, Access);

      Debug.Assert(v.TypedIdent.Type is MapType);
      MapType mt = v.TypedIdent.Type as MapType;
      Debug.Assert(mt.Arguments.Count == 1);

      Variable AccessHasOccurredVariable = GPUVerifier.MakeAccessHasOccurredVariable(v.Name, Access);
      Variable AccessOffsetVariable = RaceInstrumentationUtil.MakeOffsetVariable(v.Name, Access, verifier.IntRep.GetIntType(verifier.size_t_bits));
      Variable AccessValueVariable = RaceInstrumentationUtil.MakeValueVariable(v.Name, Access, mt.Result);
      Variable AccessBenignFlagVariable = GPUVerifier.MakeBenignFlagVariable(v.Name);
      Variable AccessAsyncHandleVariable = RaceInstrumentationUtil.MakeAsyncHandleVariable(v.Name, Access, verifier.IntRep.GetIntType(verifier.size_t_bits));

      Variable PredicateParameter = new LocalVariable(Token.NoToken, new TypedIdent(Token.NoToken, "_P", Type.Bool));
      Variable OffsetParameter = new LocalVariable(Token.NoToken, new TypedIdent(Token.NoToken, "_offset", mt.Arguments[0]));
      Variable ValueParameter = new LocalVariable(Token.NoToken, new TypedIdent(Token.NoToken, "_value", mt.Result));
      Variable ValueOldParameter = new LocalVariable(Token.NoToken, new TypedIdent(Token.NoToken, "_value_old", mt.Result));
      Variable AsyncHandleParameter = new LocalVariable(Token.NoToken, new TypedIdent(Token.NoToken, "_async_handle", verifier.IntRep.GetIntType(verifier.size_t_bits)));

      Debug.Assert(!(mt.Result is MapType));

      Block LoggingCommands = new Block(Token.NoToken, "log_access_entry", new List<Cmd>(), new ReturnCmd(Token.NoToken));

      Expr Condition = Expr.And(new IdentifierExpr(Token.NoToken, MakeTrackingVariable()), Expr.Eq(new IdentifierExpr(Token.NoToken, AccessOffsetVariable),
                                         new IdentifierExpr(Token.NoToken, OffsetParameter)));
      if(verifier.KernelArrayInfo.GetGroupSharedArrays(false).Contains(v)) {
        Condition = Expr.And(GPUVerifier.ThreadsInSameGroup(), Condition);
      }

      if(!GPUVerifyVCGenCommandLineOptions.NoBenign && Access.isReadOrWrite()) {
        Condition = Expr.And(Condition, Expr.Eq(new IdentifierExpr(Token.NoToken, AccessValueVariable), new IdentifierExpr(Token.NoToken, ValueParameter)));
      }

      Condition = Expr.And(new IdentifierExpr(Token.NoToken, PredicateParameter), Condition);

      LoggingCommands.Cmds.Add(MakeConditionalAssignment(AccessHasOccurredVariable, Condition, Expr.True));

      if (!GPUVerifyVCGenCommandLineOptions.NoBenign && Access == AccessType.WRITE) {
        LoggingCommands.Cmds.Add(MakeConditionalAssignment(AccessBenignFlagVariable,
          Condition,
          Expr.Neq(new IdentifierExpr(Token.NoToken, ValueParameter),
            new IdentifierExpr(Token.NoToken, ValueOldParameter))));
      }

      if((Access == AccessType.READ || Access == AccessType.WRITE) && verifier.ArraysAccessedByAsyncWorkGroupCopy[Access].Contains(v.Name)) {
        LoggingCommands.Cmds.Add(MakeConditionalAssignment(AccessAsyncHandleVariable,
          Condition,
          Expr.Ident(AsyncHandleParameter)));
      }

      Implementation LogAccessImplementation =
        new Implementation(Token.NoToken, "_LOG_" + Access + "_" + v.Name,
          new List<TypeVariable>(),
          LogAccessProcedure.InParams, new List<Variable>(), new List<Variable>(),
          new List<Block> { LoggingCommands } );
      GPUVerifier.AddInlineAttribute(LogAccessImplementation);

      LogAccessImplementation.Proc = LogAccessProcedure;

      verifier.Program.AddTopLevelDeclaration(LogAccessProcedure);
      verifier.Program.AddTopLevelDeclaration(LogAccessImplementation);
    }

    public override void AddRaceCheckingDeclarations() {
      base.AddRaceCheckingDeclarations();
      verifier.Program.AddTopLevelDeclaration(MakeTrackingVariable());
    }

    private static GlobalVariable MakeTrackingVariable()
    {
      return new GlobalVariable(
              Token.NoToken, new TypedIdent(Token.NoToken, "_TRACKING", Microsoft.Boogie.Type.Bool));
    }

  }
}
