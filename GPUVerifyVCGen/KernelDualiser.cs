//===-----------------------------------------------------------------------==//
//
//                GPUVerify - a Verifier for GPU Kernels
//
// This file is distributed under the Microsoft Public License.  See
// LICENSE.TXT for details.
//
//===----------------------------------------------------------------------===//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using Microsoft.Boogie;
using Microsoft.Basetypes;

namespace GPUVerify {
  class KernelDualiser {
    internal GPUVerifier verifier;

    private List<BarrierInvariantDescriptor> BarrierInvariantDescriptors;

    public KernelDualiser(GPUVerifier verifier) {
      this.verifier = verifier;
      BarrierInvariantDescriptors = new List<BarrierInvariantDescriptor>();
    }

    private string procName = null;

    internal void DualiseProcedure(Procedure proc) {
      procName = proc.Name;

      proc.Requires = DualiseRequires(proc.Requires);
      proc.Ensures = DualiseEnsures(proc.Ensures);
      proc.Modifies = DualiseModifies(proc.Modifies);

      proc.InParams = DualiseVariableSequence(proc.InParams);
      proc.OutParams = DualiseVariableSequence(proc.OutParams);

      procName = null;
    }

    private List<Requires> DualiseRequires(List<Requires> requiresSeq) {
      List<Requires> newRequires = new List<Requires>();
      foreach (Requires r in requiresSeq) {
        newRequires.Add(MakeThreadSpecificRequires(r, 1));
        if (!ContainsAsymmetricExpression(r.Condition)
            && !verifier.uniformityAnalyser.IsUniform(procName, r.Condition)) {
          newRequires.Add(MakeThreadSpecificRequires(r, 2));
        }
      }
      return newRequires;
    }

    private List<Ensures> DualiseEnsures(List<Ensures> ensuresSeq) {
      List<Ensures> newEnsures = new List<Ensures>();
      foreach (Ensures e in ensuresSeq) {
        newEnsures.Add(MakeThreadSpecificEnsures(e, 1));
        if (!ContainsAsymmetricExpression(e.Condition)
            && !verifier.uniformityAnalyser.IsUniform(procName, e.Condition)) {
          newEnsures.Add(MakeThreadSpecificEnsures(e, 2));
        }
      }
      return newEnsures;
    }

    private List<IdentifierExpr> DualiseModifies(List<IdentifierExpr> modifiesSeq) {
      List<IdentifierExpr> newModifies = new List<IdentifierExpr>();
      foreach (var m in modifiesSeq) {
        newModifies.Add((IdentifierExpr)MakeThreadSpecificExpr(m, 1));
        if (!ContainsAsymmetricExpression(m)
            && !verifier.uniformityAnalyser.IsUniform(procName, m)) {
          newModifies.Add((IdentifierExpr)MakeThreadSpecificExpr(m, 2));
        }
      }
      return newModifies;
    }

    private Expr MakeThreadSpecificExpr(Expr e, int Thread) {
      return new VariableDualiser(Thread, verifier.uniformityAnalyser, procName).
        VisitExpr(e.Clone() as Expr);
    }

    private Requires MakeThreadSpecificRequires(Requires r, int Thread) {
      Requires newR = new Requires(r.Free, MakeThreadSpecificExpr(r.Condition, Thread));
      newR.Attributes = MakeThreadSpecificAttributes(r.Attributes, Thread);
      return newR;
    }

    private Ensures MakeThreadSpecificEnsures(Ensures e, int Thread) {
      Ensures newE = new Ensures(e.Free, MakeThreadSpecificExpr(e.Condition, Thread));
      newE.Attributes = MakeThreadSpecificAttributes(e.Attributes, Thread);
      return newE;
    }

    private AssertCmd MakeThreadSpecificAssert(AssertCmd a, int Thread) {
      AssertCmd result = new AssertCmd(Token.NoToken, new VariableDualiser(Thread,
        verifier.uniformityAnalyser, procName).VisitExpr(a.Expr.Clone() as Expr),
        MakeThreadSpecificAttributes(a.Attributes, Thread));
      return result;
    }

    private AssumeCmd MakeThreadSpecificAssumeFromAssert(AssertCmd a, int Thread) {
      AssumeCmd result = new AssumeCmd(Token.NoToken, new VariableDualiser(Thread,
        verifier.uniformityAnalyser, procName).VisitExpr(a.Expr.Clone() as Expr));
      return result;
    }

    internal QKeyValue MakeThreadSpecificAttributes(QKeyValue attributes, int Thread) {
      if (attributes == null) {
        return null;
      }
      QKeyValue result = (QKeyValue)attributes.Clone();
      result.AddLast(new QKeyValue(Token.NoToken, "thread",
        new List<object>(new object[] { new LiteralExpr(Token.NoToken, BigNum.FromInt(Thread)) }), null));
      return result;
    }

    private void MakeDual(List<Cmd> cs, Cmd c) {
      if (c is CallCmd) {
        CallCmd Call = c as CallCmd;

        if (QKeyValue.FindBoolAttribute(Call.Proc.Attributes, "barrier_invariant")) {
          // There may be a predicate, and there must be an invariant expression and at least one instantiation
          Debug.Assert(Call.Ins.Count >= (2 + (verifier.uniformityAnalyser.IsUniform(Call.callee) ? 0 : 1)));
          var BIDescriptor = new UnaryBarrierInvariantDescriptor(
            verifier.uniformityAnalyser.IsUniform(Call.callee) ? Expr.True : Call.Ins[0],
            Expr.Neq(Call.Ins[verifier.uniformityAnalyser.IsUniform(Call.callee) ? 0 : 1],
              verifier.Zero(1)),
              Call.Attributes,
              this, procName, verifier);
          for (var i = 1 + (verifier.uniformityAnalyser.IsUniform(Call.callee) ? 0 : 1); i < Call.Ins.Count; i++) {
            BIDescriptor.AddInstantiationExpr(Call.Ins[i]);
          }
          BarrierInvariantDescriptors.Add(BIDescriptor);
          return;
        }

        if (QKeyValue.FindBoolAttribute(Call.Proc.Attributes, "binary_barrier_invariant")) {
          // There may be a predicate, and there must be an invariant expression and at least one pair of
          // instantiation expressions
          Debug.Assert(Call.Ins.Count >= (3 + (verifier.uniformityAnalyser.IsUniform(Call.callee) ? 0 : 1)));
          var BIDescriptor = new BinaryBarrierInvariantDescriptor(
            verifier.uniformityAnalyser.IsUniform(Call.callee) ? Expr.True : Call.Ins[0],
            Expr.Neq(Call.Ins[verifier.uniformityAnalyser.IsUniform(Call.callee) ? 0 : 1],
              verifier.Zero(1)),
              Call.Attributes,
              this, procName, verifier);
          for (var i = 1 + (verifier.uniformityAnalyser.IsUniform(Call.callee) ? 0 : 1); i < Call.Ins.Count; i += 2) {
            BIDescriptor.AddInstantiationExprPair(Call.Ins[i], Call.Ins[i + 1]);
          }
          BarrierInvariantDescriptors.Add(BIDescriptor);
          return;
        }


        if (GPUVerifier.IsBarrier(Call.Proc)) {
          // Assert barrier invariants
          foreach (var BIDescriptor in BarrierInvariantDescriptors) {
            QKeyValue SourceLocationInfo = BIDescriptor.GetSourceLocationInfo();
            cs.Add(BIDescriptor.GetAssertCmd());
            var vd = new VariableDualiser(1, verifier.uniformityAnalyser, procName);
            if (GPUVerifyVCGenCommandLineOptions.BarrierAccessChecks) {
              foreach (Expr AccessExpr in BIDescriptor.GetAccessedExprs()) {
                var Assert = new AssertCmd(Token.NoToken, AccessExpr, MakeThreadSpecificAttributes(SourceLocationInfo,1));
                Assert.Attributes = new QKeyValue(Token.NoToken, "barrier_invariant_access_check",
                  new List<object> { Expr.True }, Assert.Attributes);
                cs.Add(vd.VisitAssertCmd(Assert));
              }
            }
          }
        }

        List<Expr> uniformNewIns = new List<Expr>();
        List<Expr> nonUniformNewIns = new List<Expr>();

        for (int i = 0; i < Call.Ins.Count; i++) {
          if (verifier.uniformityAnalyser.knowsOf(Call.callee) && verifier.uniformityAnalyser.IsUniform(Call.callee, verifier.uniformityAnalyser.GetInParameter(Call.callee, i))) {
            uniformNewIns.Add(Call.Ins[i]);
          }
          else if(!verifier.OnlyThread2.Contains(Call.callee)) {
            nonUniformNewIns.Add(new VariableDualiser(1, verifier.uniformityAnalyser, procName).VisitExpr(Call.Ins[i]));
          }
        }
        for (int i = 0; i < Call.Ins.Count; i++) {
          if (
            !(verifier.uniformityAnalyser.knowsOf(Call.callee) && verifier.uniformityAnalyser.IsUniform(Call.callee, verifier.uniformityAnalyser.GetInParameter(Call.callee, i)))
            && !verifier.OnlyThread1.Contains(Call.callee)) {
            nonUniformNewIns.Add(new VariableDualiser(2, verifier.uniformityAnalyser, procName).VisitExpr(Call.Ins[i]));
          }
        }

        List<Expr> newIns = uniformNewIns;
        newIns.AddRange(nonUniformNewIns);

        List<IdentifierExpr> uniformNewOuts = new List<IdentifierExpr>();
        List<IdentifierExpr> nonUniformNewOuts = new List<IdentifierExpr>();
        for (int i = 0; i < Call.Outs.Count; i++) {
          if (verifier.uniformityAnalyser.knowsOf(Call.callee) && verifier.uniformityAnalyser.IsUniform(Call.callee, verifier.uniformityAnalyser.GetOutParameter(Call.callee, i))) {
            uniformNewOuts.Add(Call.Outs[i]);
          }
          else {
            nonUniformNewOuts.Add(new VariableDualiser(1, verifier.uniformityAnalyser, procName).VisitIdentifierExpr(Call.Outs[i].Clone() as IdentifierExpr) as IdentifierExpr);
          }

        }
        for (int i = 0; i < Call.Outs.Count; i++) {
          if (!(verifier.uniformityAnalyser.knowsOf(Call.callee) && verifier.uniformityAnalyser.IsUniform(Call.callee, verifier.uniformityAnalyser.GetOutParameter(Call.callee, i)))) {
            nonUniformNewOuts.Add(new VariableDualiser(2, verifier.uniformityAnalyser, procName).VisitIdentifierExpr(Call.Outs[i].Clone() as IdentifierExpr) as IdentifierExpr);
          }
        }

        List<IdentifierExpr> newOuts = uniformNewOuts;
        newOuts.AddRange(nonUniformNewOuts);

        CallCmd NewCallCmd = new CallCmd(Call.tok, Call.callee, newIns, newOuts);

        NewCallCmd.Proc = Call.Proc;

        NewCallCmd.Attributes = Call.Attributes;

        if (NewCallCmd.callee.StartsWith("_LOG_ATOMIC"))
        {
          QKeyValue curr = NewCallCmd.Attributes;
          if (curr.Key.StartsWith("arg"))
            NewCallCmd.Attributes = new QKeyValue(Token.NoToken, curr.Key, new List<object>(new object[]{Dualise(curr.Params[0] as Expr,1)}), curr.Next);
          for (curr = NewCallCmd.Attributes; curr.Next != null; curr = curr.Next)
            if (curr.Next.Key.StartsWith("arg"))
            {
              curr.Next = new QKeyValue(Token.NoToken, curr.Next.Key, new List<object>(new object[]{Dualise(curr.Next.Params[0] as Expr,1)}), curr.Next.Next);
            }
        }
        else if (NewCallCmd.callee.StartsWith("_CHECK_ATOMIC"))
        {
          QKeyValue curr = NewCallCmd.Attributes;
          if (curr.Key.StartsWith("arg"))
            NewCallCmd.Attributes = new QKeyValue(Token.NoToken, curr.Key, new List<object>(new object[]{Dualise(curr.Params[0] as Expr, 2)}), curr.Next);
          for (curr = NewCallCmd.Attributes; curr.Next != null; curr = curr.Next)
            if (curr.Next.Key.StartsWith("arg"))
            {
              curr.Next = new QKeyValue(Token.NoToken, curr.Next.Key, new List<object>(new object[]{Dualise(curr.Next.Params[0] as Expr,2)}), curr.Next.Next);
            }
        }

        cs.Add(NewCallCmd);

        if (GPUVerifier.IsBarrier(Call.Proc)) {
          foreach (var BIDescriptor in BarrierInvariantDescriptors) {
            foreach (var Instantiation in BIDescriptor.GetInstantiationCmds()) {
              cs.Add(Instantiation);
            }
          }
          BarrierInvariantDescriptors.Clear();
        }

      }
      else if (c is AssignCmd) {
        AssignCmd assign = c as AssignCmd;

        var vd1 = new VariableDualiser(1, verifier.uniformityAnalyser, procName);
        var vd2 = new VariableDualiser(2, verifier.uniformityAnalyser, procName);

        List<AssignLhs> lhss1 = new List<AssignLhs>();
        List<AssignLhs> lhss2 = new List<AssignLhs>();

        List<Expr> rhss1 = new List<Expr>();
        List<Expr> rhss2 = new List<Expr>();
 
        foreach(var pair in assign.Lhss.Zip(assign.Rhss)) {
          if(pair.Item1 is SimpleAssignLhs &&
            verifier.uniformityAnalyser.IsUniform(procName, 
            (pair.Item1 as SimpleAssignLhs).AssignedVariable.Name)) {
            lhss1.Add(pair.Item1);
            rhss1.Add(pair.Item2);
          } else {
            lhss1.Add(vd1.Visit(pair.Item1.Clone() as AssignLhs) as AssignLhs);
            lhss2.Add(vd2.Visit(pair.Item1.Clone() as AssignLhs) as AssignLhs);
            rhss1.Add(vd1.VisitExpr(pair.Item2.Clone() as Expr));
            rhss2.Add(vd2.VisitExpr(pair.Item2.Clone() as Expr));
          }
        }

        Debug.Assert(lhss1.Count > 0);
        cs.Add(new AssignCmd(Token.NoToken, lhss1, rhss1));

        if(lhss2.Count > 0) {
          cs.Add(new AssignCmd(Token.NoToken, lhss2, rhss2));
        }

      }
      else if (c is HavocCmd) {
        HavocCmd havoc = c as HavocCmd;
        Debug.Assert(havoc.Vars.Count() == 1);

        HavocCmd newHavoc;

        newHavoc = new HavocCmd(havoc.tok, new List<IdentifierExpr>(new IdentifierExpr[] {
                    (IdentifierExpr)(new VariableDualiser(1, verifier.uniformityAnalyser, procName).VisitIdentifierExpr(havoc.Vars[0].Clone() as IdentifierExpr)),
                    (IdentifierExpr)(new VariableDualiser(2, verifier.uniformityAnalyser, procName).VisitIdentifierExpr(havoc.Vars[0].Clone() as IdentifierExpr))
                }));

        cs.Add(newHavoc);
      }
      else if (c is AssertCmd) {
        AssertCmd a = c as AssertCmd;

        if (QKeyValue.FindBoolAttribute(a.Attributes, "sourceloc")
          || QKeyValue.FindBoolAttribute(a.Attributes, "block_sourceloc")
          || QKeyValue.FindBoolAttribute(a.Attributes, "array_bounds")) {
          // This is just a location marker, so we do not dualise it
          cs.Add(new AssertCmd(Token.NoToken, new VariableDualiser(1, verifier.uniformityAnalyser, procName).VisitExpr(a.Expr.Clone() as Expr),
            (QKeyValue)a.Attributes.Clone()));
        }
        else {
          var isUniform = verifier.uniformityAnalyser.IsUniform(procName, a.Expr);
          cs.Add(MakeThreadSpecificAssert(a, 1));
          if (!GPUVerifyVCGenCommandLineOptions.AsymmetricAsserts && !ContainsAsymmetricExpression(a.Expr) && !isUniform) {
            cs.Add(MakeThreadSpecificAssert(a, 2));
          }
        }
      }
      else if (c is AssumeCmd) {
        AssumeCmd ass = c as AssumeCmd;

        if (QKeyValue.FindStringAttribute(ass.Attributes, "captureState") != null) {
          cs.Add(c);
        } else if (QKeyValue.FindBoolAttribute(ass.Attributes, "backedge")) {
          AssumeCmd newAss = new AssumeCmd(c.tok, Expr.Or(new VariableDualiser(1, verifier.uniformityAnalyser, procName).VisitExpr(ass.Expr.Clone() as Expr),
              new VariableDualiser(2, verifier.uniformityAnalyser, procName).VisitExpr(ass.Expr.Clone() as Expr)));
          newAss.Attributes = ass.Attributes;
          cs.Add(newAss);
        }
        else if (QKeyValue.FindBoolAttribute(ass.Attributes, "atomic_refinement")) {

          // Generate the following:
          // havoc v$1, v$2;
          // assume !_USED[offset$1][v$1];
          // _USED[offset$1][v$1] := true;
          // assume !_USED[offset$2][v$2];
          // _USED[offset$2][v$2] := true;

          Expr variable = QKeyValue.FindExprAttribute(ass.Attributes, "variable");
          Expr offset = QKeyValue.FindExprAttribute(ass.Attributes, "offset");

          List<Expr> offsets = (new int[] { 1, 2 }).Select(x => new VariableDualiser(x, verifier.uniformityAnalyser, procName).VisitExpr(offset.Clone() as Expr)).ToList();
          List<Expr> vars = (new int[] { 1, 2 }).Select(x => new VariableDualiser(x, verifier.uniformityAnalyser, procName).VisitExpr(variable.Clone() as Expr)).ToList();
          IdentifierExpr arrayref = new IdentifierExpr(Token.NoToken, verifier.FindOrCreateUsedMap(QKeyValue.FindStringAttribute(ass.Attributes, "arrayref"), vars[0].Type));

          foreach (int i in (new int[] { 0, 1 })) {
            AssumeCmd newAss = new AssumeCmd(c.tok, Expr.Not(new NAryExpr(Token.NoToken, new MapSelect(Token.NoToken, 1),
              new List<Expr> { new NAryExpr(Token.NoToken, new MapSelect(Token.NoToken, 1),
                new List<Expr> { arrayref, offsets[i] }),
                vars[i] })));

            cs.Add(newAss);

            var lhs = new MapAssignLhs(Token.NoToken, new MapAssignLhs(Token.NoToken, new SimpleAssignLhs(Token.NoToken, arrayref),
                new List<Expr> { offsets[i] }), new List<Expr> { vars[i] });
            AssignCmd assign = new AssignCmd(c.tok,
              new List<AssignLhs> { lhs },
              new List<Expr> {Expr.True});

            cs.Add(assign);

          }

        }
        else {
          var isUniform = verifier.uniformityAnalyser.IsUniform(procName, ass.Expr);
          AssumeCmd newAss = new AssumeCmd(c.tok, new VariableDualiser(1, verifier.uniformityAnalyser, procName).VisitExpr(ass.Expr.Clone() as Expr));
          if (!ContainsAsymmetricExpression(ass.Expr) && !isUniform) {
            newAss.Expr = Expr.And(newAss.Expr, new VariableDualiser(2, verifier.uniformityAnalyser, procName).VisitExpr(ass.Expr.Clone() as Expr));
          }
          newAss.Attributes = ass.Attributes;
          cs.Add(newAss);
        }
      }
      else {
        Debug.Assert(false);
      }
    }

    private Block MakeDual(Block b) {
      var newCmds = new List<Cmd>();
      foreach (Cmd c in b.Cmds) {
        MakeDual(newCmds, c);
      }
      b.Cmds = newCmds;
      return b;
    }

    private List<PredicateCmd> MakeDualInvariants(List<PredicateCmd> originalInvariants) {
      List<PredicateCmd> result = new List<PredicateCmd>();
      foreach (PredicateCmd p in originalInvariants) {
        {
          PredicateCmd newP = new AssertCmd(p.tok,
              Dualise(p.Expr, 1));
          newP.Attributes = p.Attributes;
          result.Add(newP);
        }
        if (!ContainsAsymmetricExpression(p.Expr)
            && !verifier.uniformityAnalyser.IsUniform(procName, p.Expr)) {
          PredicateCmd newP = new AssertCmd(p.tok, Dualise(p.Expr, 2));
          newP.Attributes = p.Attributes;
          result.Add(newP);
        }
      }

      return result;
    }

    private void MakeDualLocalVariables(Implementation impl) {
      List<Variable> NewLocalVars = new List<Variable>();

      foreach (LocalVariable v in impl.LocVars) {
        if (verifier.uniformityAnalyser.IsUniform(procName, v.Name)) {
          NewLocalVars.Add(v);
        }
        else {
          NewLocalVars.Add(
              new VariableDualiser(1, verifier.uniformityAnalyser, procName).VisitVariable(v.Clone() as Variable));
          NewLocalVars.Add(
              new VariableDualiser(2, verifier.uniformityAnalyser, procName).VisitVariable(v.Clone() as Variable));
        }
      }

      impl.LocVars = NewLocalVars;
    }

    private static bool ContainsAsymmetricExpression(Expr expr) {
      AsymmetricExpressionFinder finder = new AsymmetricExpressionFinder();
      finder.VisitExpr(expr);
      return finder.foundAsymmetricExpr();
    }

    private List<Variable> DualiseVariableSequence(List<Variable> seq) {
      List<Variable> uniform = new List<Variable>();
      List<Variable> nonuniform = new List<Variable>();

      foreach (Variable v in seq) {
        if (verifier.uniformityAnalyser.IsUniform(procName, v.Name)) {
          uniform.Add(v);
        }
        else {
          nonuniform.Add(new VariableDualiser(1, verifier.uniformityAnalyser, procName).VisitVariable((Variable)v.Clone()));
        }
      }

      foreach (Variable v in seq) {
        if (!verifier.uniformityAnalyser.IsUniform(procName, v.Name)) {
          nonuniform.Add(new VariableDualiser(2, verifier.uniformityAnalyser, procName).VisitVariable((Variable)v.Clone()));
        }
      }

      List<Variable> result = uniform;
      result.AddRange(nonuniform);
      return result;
    }


    internal void DualiseImplementation(Implementation impl) {
      procName = impl.Name;
      impl.InParams = DualiseVariableSequence(impl.InParams);
      impl.OutParams = DualiseVariableSequence(impl.OutParams);
      MakeDualLocalVariables(impl);
      impl.Blocks = new List<Block>(impl.Blocks.Select(MakeDual));
      procName = null;
    }

    private Expr Dualise(Expr expr, int thread) {
      return new VariableDualiser(thread, verifier.uniformityAnalyser, procName).VisitExpr(expr.Clone() as Expr);
    }

    private int UpdateDeclarationsAndCountTotal(List<Declaration> decls) {
        var newDecls = verifier.Program.TopLevelDeclarations.Where(d => !decls.Contains(d));
        decls.AddRange(newDecls.ToList());
        return decls.Count();
    }

    internal void DualiseKernel()
    {
        List<Declaration> NewTopLevelDeclarations = new List<Declaration>();

        // This loop really does have to be a "for(i ...)" loop.  The reason is
        // that dualisation may add additional functions to the program, which
        // get put into the program's top level declarations and also need to
        // be dualised.
        var decls = verifier.Program.TopLevelDeclarations.ToList();
        for(int i = 0; i < UpdateDeclarationsAndCountTotal(decls); i++)
        {
            Declaration d = decls[i];

            if (d is Axiom) {

              VariableDualiser vd1 = new VariableDualiser(1, null, null);
              VariableDualiser vd2 = new VariableDualiser(2, null, null);
              Axiom NewAxiom1 = vd1.VisitAxiom(d.Clone() as Axiom);
              Axiom NewAxiom2 = vd2.VisitAxiom(d.Clone() as Axiom);
              NewTopLevelDeclarations.Add(NewAxiom1);

              // Test whether dualisation had any effect by seeing whether the new
              // axioms are syntactically indistinguishable.  If they are, then there
              // is no point adding the second axiom.
              if(!NewAxiom1.ToString().Equals(NewAxiom2.ToString())) {
                NewTopLevelDeclarations.Add(NewAxiom2);
              }
              continue;
            }

            if (d is Procedure)
            {
                DualiseProcedure(d as Procedure);
                NewTopLevelDeclarations.Add(d);
                continue;
            }

            if (d is Implementation)
            {
                DualiseImplementation(d as Implementation);
                NewTopLevelDeclarations.Add(d);
                continue;
            }

            if (d is Variable && ((d as Variable).IsMutable ||
                GPUVerifier.IsThreadLocalIdConstant(d as Variable) ||
                (GPUVerifier.IsGroupIdConstant(d as Variable) && !GPUVerifyVCGenCommandLineOptions.OnlyIntraGroupRaceChecking))) {
              var v = d as Variable;

              if (v.Name.Contains("_NOT_ACCESSED_") || v.Name.Contains("_ARRAY_OFFSET")) {
                NewTopLevelDeclarations.Add(v);
                continue;
              }
              if (QKeyValue.FindBoolAttribute(v.Attributes, "atomic_usedmap"))
              {
                NewTopLevelDeclarations.Add(v);
                continue;
              }

              if (verifier.KernelArrayInfo.GetGlobalArrays(true).Contains(v)) {
                NewTopLevelDeclarations.Add(v);
                continue;
              }

              if (verifier.KernelArrayInfo.GetGroupSharedArrays(true).Contains(v)) {
                if(!GPUVerifyVCGenCommandLineOptions.OnlyIntraGroupRaceChecking) {
                  Variable newV = new GlobalVariable(Token.NoToken, new TypedIdent(Token.NoToken,
                      v.Name, new MapType(Token.NoToken, new List<TypeVariable>(),
                      new List<Microsoft.Boogie.Type> { Microsoft.Boogie.Type.GetBvType(1) },
                      v.TypedIdent.Type)));
                  newV.Attributes = v.Attributes;
                  NewTopLevelDeclarations.Add(newV);
                } else {
                  NewTopLevelDeclarations.Add(v);
                }
                continue;
              }

              NewTopLevelDeclarations.Add(new VariableDualiser(1, null, null).VisitVariable((Variable)v.Clone()));
              if (!QKeyValue.FindBoolAttribute(v.Attributes, "race_checking")) {
                NewTopLevelDeclarations.Add(new VariableDualiser(2, null, null).VisitVariable((Variable)v.Clone()));
              }

              continue;
            }

            NewTopLevelDeclarations.Add(d);
        }

        verifier.Program.TopLevelDeclarations = NewTopLevelDeclarations;
    }
  }

  class ThreadInstantiator : Duplicator {

    private Expr InstantiationExpr;
    private int Thread;
    private UniformityAnalyser Uni;
    private string ProcName;

    internal ThreadInstantiator(Expr InstantiationExpr, int Thread,
        UniformityAnalyser Uni, string ProcName) {
      this.InstantiationExpr = InstantiationExpr;
      this.Thread = Thread;
      this.Uni = Uni;
      this.ProcName = ProcName;
    }

    public override Expr VisitIdentifierExpr(IdentifierExpr node) {
      Debug.Assert(!(node.Decl is Formal));

      if (GPUVerifier.IsThreadLocalIdConstant(node.Decl)) {
        Debug.Assert(node.Decl.Name.Equals(GPUVerifier._X.Name));
        return InstantiationExpr.Clone() as Expr;
      }

      if (node.Decl is Constant ||
          QKeyValue.FindBoolAttribute(node.Decl.Attributes, "global") ||
          QKeyValue.FindBoolAttribute(node.Decl.Attributes, "group_shared") ||
          (Uni != null && Uni.IsUniform(ProcName, node.Decl.Name))) {
        return base.VisitIdentifierExpr(node);
      }

      Console.WriteLine("Expression " + node + " is not valid as part of a barrier invariant: it cannot be instantiated by arbitrary threads.");
      Console.WriteLine("Check that it is not a thread local variable, or a thread local (rather than __local or __global) array.");
      Console.WriteLine("In particular, if you have a local variable called tid, which you initialise to e.g. get_local_id(0), this will not work:");
      Console.WriteLine("  you need to use get_local_id(0) directly.");
      Environment.Exit(1);
      return null;
    }

    private bool InstantiationExprIsThreadId() {
      return (InstantiationExpr is IdentifierExpr) &&
        ((IdentifierExpr)InstantiationExpr).Decl.Name.Equals(GPUVerifier.MakeThreadId("X", Thread).Name);
    }
  }



  class ThreadPairInstantiator : Duplicator {

    private GPUVerifier verifier;
    private Tuple<Expr, Expr> InstantiationExprs;
    private int Thread;

    internal ThreadPairInstantiator(GPUVerifier verifier, Expr InstantiationExpr1, Expr InstantiationExpr2, int Thread) {
      this.verifier = verifier;
      this.InstantiationExprs = new Tuple<Expr, Expr>(InstantiationExpr1, InstantiationExpr2);
      this.Thread = Thread;
    }

    public override Expr VisitIdentifierExpr(IdentifierExpr node) {
      Debug.Assert(!(node.Decl is Formal));

      if (GPUVerifier.IsThreadLocalIdConstant(node.Decl)) {
        Debug.Assert(node.Decl.Name.Equals(GPUVerifier._X.Name));
        return InstantiationExprs.Item1.Clone() as Expr;
      }

      if (node.Decl is Constant ||
          verifier.KernelArrayInfo.GetGroupSharedArrays(true).Contains(node.Decl) ||
          verifier.KernelArrayInfo.GetGlobalArrays(true).Contains(node.Decl)) {
        return base.VisitIdentifierExpr(node);
      }

      Console.WriteLine("Expression " + node + " is not valid as part of a barrier invariant: it cannot be instantiated by arbitrary threads.");
      Console.WriteLine("Check that it is not a thread local variable, or a thread local (rather than __local or __global) array.");
      Console.WriteLine("In particular, if you have a local variable called tid, which you initialise to e.g. get_local_id(0), this will not work:");
      Console.WriteLine("  you need to use get_local_id(0) directly.");
      Environment.Exit(1);
      return null;
    }

    public override Expr VisitNAryExpr(NAryExpr node) {
      if (node.Fun is FunctionCall) {
        FunctionCall call = node.Fun as FunctionCall;

        // Alternate instantiation order for "other thread" functions.
        // Note that we do not alternatve the "Thread" field, as we are not switching the
        // thread for which instantiation is being performed
        if (VariableDualiser.otherFunctionNames.Contains(call.Func.Name)) {
          return new ThreadPairInstantiator(verifier, InstantiationExprs.Item2, InstantiationExprs.Item1, Thread)
            .VisitExpr(node.Args[0]);
        }

      }

      return base.VisitNAryExpr(node);
    }


  }



}
