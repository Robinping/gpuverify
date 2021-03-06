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
  class OriginalRaceInstrumenter : RaceInstrumenter
  {

    internal OriginalRaceInstrumenter(GPUVerifier verifier) : base(verifier) { }

    protected override void AddLogAccessProcedure(Variable v, AccessType Access) {

      // This array should be included in the set of global or group shared arrays that
      // are *not* disabled
      Debug.Assert(verifier.KernelArrayInfo.GetGlobalAndGroupSharedArrays(false).Contains(v));

      Procedure LogAccessProcedure = MakeLogAccessProcedureHeader(v, Access);

      Debug.Assert(v.TypedIdent.Type is MapType);
      MapType mt = v.TypedIdent.Type as MapType;
      Debug.Assert(mt.Arguments.Count == 1);

      Variable AccessHasOccurredVariable = GPUVerifier.MakeAccessHasOccurredVariable(v.Name, Access);
      Variable AccessOffsetVariable = RaceInstrumentationUtil.MakeOffsetVariable(v.Name, Access, verifier.IntRep.GetIntType(verifier.size_t_bits));
      Variable AccessValueVariable = RaceInstrumentationUtil.MakeValueVariable(v.Name, Access, mt.Result);
      Variable AccessBenignFlagVariable = GPUVerifier.MakeBenignFlagVariable(v.Name);
      Variable AccessAsyncHandleVariable = RaceInstrumentationUtil.MakeAsyncHandleVariable(v.Name, Access, verifier.IntRep.GetIntType(verifier.size_t_bits));

      Variable PredicateParameter = new LocalVariable(Token.NoToken, new TypedIdent(Token.NoToken, "_P", Microsoft.Boogie.Type.Bool));
      Variable OffsetParameter = new LocalVariable(Token.NoToken, new TypedIdent(Token.NoToken, "_offset", mt.Arguments[0]));
      Variable ValueParameter = new LocalVariable(Token.NoToken, new TypedIdent(Token.NoToken, "_value", mt.Result));
      Variable ValueOldParameter = new LocalVariable(Token.NoToken, new TypedIdent(Token.NoToken, "_value_old", mt.Result));
      Variable AsyncHandleParameter = new LocalVariable(Token.NoToken, new TypedIdent(Token.NoToken, "_async_handle", verifier.IntRep.GetIntType(verifier.size_t_bits)));

      Debug.Assert(!(mt.Result is MapType));

      List<Variable> locals = new List<Variable>();
      Variable TrackVariable = new LocalVariable(Token.NoToken,
        new TypedIdent(Token.NoToken, "track", Microsoft.Boogie.Type.Bool));
      locals.Add(TrackVariable);

      List<BigBlock> bigblocks = new List<BigBlock>();

      List<Cmd> simpleCmds = new List<Cmd>();

      // Havoc tracking variable
      simpleCmds.Add(new HavocCmd(v.tok, new List<IdentifierExpr>(new IdentifierExpr[] { new IdentifierExpr(v.tok, TrackVariable) })));

      Expr Condition = Expr.And(new IdentifierExpr(v.tok, PredicateParameter),
        new IdentifierExpr(v.tok, TrackVariable));

      if(verifier.KernelArrayInfo.GetGroupSharedArrays(false).Contains(v)) {
        Condition = Expr.And(GPUVerifier.ThreadsInSameGroup(), Condition);
      }

      simpleCmds.Add(MakeConditionalAssignment(AccessHasOccurredVariable,
          Condition, Expr.True));
      simpleCmds.Add(MakeConditionalAssignment(AccessOffsetVariable,
          Condition,
          new IdentifierExpr(v.tok, OffsetParameter)));
      if (!GPUVerifyVCGenCommandLineOptions.NoBenign && Access.isReadOrWrite()) {
        simpleCmds.Add(MakeConditionalAssignment(AccessValueVariable,
          Condition,
          new IdentifierExpr(v.tok, ValueParameter)));
      }
      if (!GPUVerifyVCGenCommandLineOptions.NoBenign && Access == AccessType.WRITE) {
        simpleCmds.Add(MakeConditionalAssignment(AccessBenignFlagVariable,
          Condition,
          Expr.Neq(new IdentifierExpr(v.tok, ValueParameter),
            new IdentifierExpr(v.tok, ValueOldParameter))));
      }
      if((Access == AccessType.READ || Access == AccessType.WRITE) &&
        verifier.ArraysAccessedByAsyncWorkGroupCopy[Access].Contains(v.Name)) {
        simpleCmds.Add(MakeConditionalAssignment(AccessAsyncHandleVariable,
          Condition,
          Expr.Ident(AsyncHandleParameter)));
      }

      bigblocks.Add(new BigBlock(v.tok, "_LOG_" + Access + "", simpleCmds, null, null));

      Implementation LogAccessImplementation = new Implementation(v.tok, "_LOG_" + Access + "_" + v.Name, new List<TypeVariable>(), LogAccessProcedure.InParams, new List<Variable>(), locals, new StmtList(bigblocks, v.tok));
      GPUVerifier.AddInlineAttribute(LogAccessImplementation);

      LogAccessImplementation.Proc = LogAccessProcedure;

      verifier.Program.AddTopLevelDeclaration(LogAccessProcedure);
      verifier.Program.AddTopLevelDeclaration(LogAccessImplementation);
    }

  }
}
