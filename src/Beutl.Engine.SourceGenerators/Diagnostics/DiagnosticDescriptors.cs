using Microsoft.CodeAnalysis;

namespace Beutl.Engine.SourceGenerators.Diagnostics;

public static class DiagnosticDescriptors
{
    public static readonly DiagnosticDescriptor MissingPartial = new(
        id: "BESG001",
        title: "Partial declaration required",
        messageFormat: "Type '{0}' must be declared partial to generate Resource nested classes",
        category: "Beutl.Engine.SourceGenerators",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor FallbackMissingPartial = new(
        id: "BESG002",
        title: "Partial declaration required for IFallback",
        messageFormat: "Type '{0}' must be declared partial to generate IFallback implementation",
        category: "Beutl.Engine.SourceGenerators",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor CapturingMetadataCallback = new(
        id: "BESG003",
        title: "Render metadata callback reads more than the render node that declares it",
        messageFormat:
            "'{0}.{1}' keys its compiled plan by which method the callback is, not by what the callback "
            + "reads, so the callback may read the render node it is written inside and must reach nothing "
            + "else; {2}. Declare the callback static, write it as a lambda closing over nothing but the "
            + "declaring node or as a method group naming one of that node's own methods, and carry every "
            + "other changing value through the state-passing overload or a bound render resource.",
        category: "Beutl.Engine.SourceGenerators",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "A render metadata callback is re-run whenever metadata is resolved - forward bounds, backward "
            + "region of interest, scale reevaluation, hit testing, cache lookup - and the plan it compiles "
            + "into is keyed by which method the callback is. Two recordings of one declaration therefore "
            + "share a plan, and the callback is re-run over each. Reading the RenderNode that declares the "
            + "callback is admitted on that footing: the node arrives as the delegate's own target rather "
            + "than as a closure field, marking the node changed re-records it, and an answer of the node's "
            + "that moves between recording and graph-wide metadata resolution fails the request at the "
            + "recorded-answer cross-check instead of silently winning. That admits both spellings of it, a "
            + "lambda closing over nothing but its own node and a method group naming a method of that "
            + "node, because the two hand the runtime the same delegate and differ only in how the mapping "
            + "was written. A captured local or parameter has none of that - nothing re-records when it is "
            + "assigned, and the plan compiled for its first answer is replayed for the second - and "
            + "neither does an enclosing instance that is not a node, whose state no change marking covers, "
            + "nor a receiver the author holds somewhere else. The runtime identity validator cannot stand "
            + "in for this rule either: it reads the delegate's target alone, and a closure over anything "
            + "besides the declaring instance arrives as a compiler display class that none of its type "
            + "tests answer for.");

    public static readonly DiagnosticDescriptor StaticStateMetadataCallback = new(
        id: "BESG004",
        title: "Render metadata callback reads static state that is not proven constant",
        messageFormat:
            "'{0}.{1}' keys its compiled plan by which callback this is, not by what the callback reads, "
            + "so the callback has to answer the same way every time it runs; this one reaches the {2} "
            + "'{3}', and {4}. Carry the value through the state-passing overload or a bound render "
            + "resource, or make '{3}' answer the same way every time. This check reads the callback's own "
            + "body - a lambda's, or the one a method group names - and follows what that body names and "
            + "what it runs without naming, to a bounded depth: the static methods, the constructors, the "
            + "user-defined operators and conversions, the extension methods it calls in instance form - "
            + "which are static methods however they are spelled - and any member - a method, a property "
            + "or an indexer accessor, the Add a collection initialiser spells as an element included - "
            + "called on an instance the expression makes right there. What a callee whose body has "
            + "no source here reads, what an instance member computes on a receiver the call did "
            + "not make, and what a call this build removes would have read - a [Conditional] one "
            + "whose symbol is not defined here, which is why this rule can answer differently in "
            + "Debug and in Release - are still invisible, so it staying silent is not proof that "
            + "the callback is state-free.",
        category: "Beutl.Engine.SourceGenerators",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "Proving a callback state-free in general is not possible, and this rule does not try. It reads "
            + "only the static fields, properties and events the callback body names directly. A static field "
            + "is accepted when it is const, or readonly and of a type whose instances carry no writable "
            + "state: an enum, a primitive, string, decimal, DateTime, a RenderResourceSlot<T>, or a "
            + "readonly struct or sealed class whose every instance field, inherited ones included, is "
            + "itself readonly and of such a type, walked to a bounded depth. That walk is only run where "
            + "the field list is the whole type - a type declared in this compilation, read from its "
            + "declaration, or a struct, whose fields a compilation imports whatever their accessibility. A "
            + "class from another assembly is imported down to its public and protected members, so its "
            + "field list is a floor and not the type; a static readonly field of one is reported however "
            + "immutable the visible members look, and the fix is to declare the type where this rule can "
            + "read it or to carry the value through the state-passing overload. readonly on its own fixes "
            + "the reference and not the object behind it, so a static readonly field of a type carrying a "
            + "writable field - its own, one it reaches, or the delegate field a field-like event stands "
            + "for, which a source type's member list leaves out, carrying the event and its accessors "
            + "in its place - is reported, and so is one of an unsealed class, which a subclass can add "
            + "state to, or of a delegate, whose target this rule cannot read. An event written with its "
            + "own accessors stores nothing of itself and does not count against its type. A static "
            + "property is accepted only when its getter is proven to yield the same value on every "
            + "read: an expression-bodied getter, a getter whose body is a single return, or the "
            + "initialiser of a get-only auto-property, whose expression is a compile-time constant, an "
            + "enum member, default, or a static readonly field that same test accepts. Every other getter "
            + "is reported, including one whose source is not available here, because a metadata callback "
            + "reading external mutable static state is the hazard this rule exists for; having no setter "
            + "is not on its own evidence that a getter answers the same way twice. A field-like static "
            + "event is reported wherever the callback names it, on either side of the +=: its value is a "
            + "subscriber list that any subscription anywhere in the program rewrites. Reading that list "
            + "back is legal only inside the declaring type, which is where a callback written beside the "
            + "event, and any helper the walk follows into it, both sit. An event written with its own "
            + "accessors is read through the accessor the += or -= actually runs, the same way a property "
            + "is read through the accessor a reference runs, so one whose accessors store nothing holds "
            + "nothing for a later subscription to differ by, while one whose accessors write a mutable "
            + "static is reported for that write. An event whose accessors have no source here is "
            + "reported, because metadata carries real accessors whether the author wrote them or the "
            + "compiler did and nothing there says which. All of that is read out "
            + "of the callback's own body, and a method group names a body as surely as a lambda writes "
            + "one, so both are read: the method a group names is followed to its declaration and walked. "
            + "A method group whose body has no source in this compilation is reported, because that body "
            + "is the whole of the callback and silence would say the rule looked when it looked at "
            + "nothing - the position the static field rule already takes for a type whose state was never "
            + "imported. From inside a body the walk follows the static methods that body names, and the "
            + "methods those name, to a bounded depth; a chain longer than the bound is reported rather "
            + "than accepted, so the bound can cost a diagnostic and never hide one. A called method whose "
            + "body has no source here is where the walk stops without reporting: the rule did inspect the "
            + "callback, so the callee is a bound on an inspected callback rather than an uninspected one, "
            + "and reporting it would reject every callback that names Math.Clamp. The walk also enters "
            + "what a body runs without naming, on those same terms and under that same bound: a "
            + "constructor - its own body, the constructor it chains to, the base constructor it runs "
            + "without saying so, and the instance field and property initialisers that run with it - and "
            + "a user-defined operator or conversion, which the source spells as punctuation or, for an "
            + "implicit conversion, as nothing at all. A collection initialiser on an object creation runs "
            + "an Add per element, in the single-argument form and in the braced multi-argument one, and "
            + "the element binds to nothing that names it, so that Add is walked as well; braces filling a "
            + "member rather than the creation - new Outer { Inner = { value } } - call Add on an instance "
            + "the callback did not make and are the receiver case below. An extension method the body "
            + "calls in instance "
            + "form is walked too, because writing value.Shift() rather than Extensions.Shift(value) "
            + "changes only the spelling: the method that runs is static, and every value it reads - the "
            + "receiver included - is an argument the call site passed it, so following it asks nothing "
            + "about an instance. An instance member is walked on those same terms when this rule can "
            + "point at the object creation that made the receiver: an object creation names the exact "
            + "type it makes, so the member the call binds to is the member that runs. That creation is "
            + "the one written at the call site, the one initialising a readonly field the callback "
            + "reaches on its own - named directly or through one hop of a set-less property whose getter "
            + "names that field and nothing else - or the one initialising a local nothing assigns again. "
            + "Its constructor is walked only where the creation itself is written inside the body being "
            + "walked, because a constructor that ran before the callback was handed over froze one value "
            + "into one instance and has no later recording to answer differently at, while one the "
            + "callback runs on every invocation reads its statics afresh each time; the members called "
            + "on the instance are walked either way. A property or "
            + "indexer contributes the accessor the reference actually runs - the getter for a read, the "
            + "setter for an assignment - and an auto-property's accessor, having no body, is the "
            + "no-source case. A receiver the call did not make is where the walk stops, and not only "
            + "because it cannot be identified: these callbacks are handed the objects they work through, "
            + "so a member called on one of those is the engine behind it, whose loggers, shared contexts "
            + "and pools say nothing about whether the callback answers the same way twice. What is still "
            + "invisible to it: what a callee with no source here reads, what an instance member "
            + "computes on a receiver the call did not make, and what a call this build removes "
            + "would have read, because a [Conditional] helper whose symbol is not defined here is "
            + "in no program this build ships - so a callback whose only unproven read is inside "
            + "one is reported in Debug and accepted in Release, which is the honest answer for "
            + "each. Treat silence as the absence of the shape authors usually write, not as a "
            + "purity proof.");

    public static readonly DiagnosticDescriptor UnmarkedRenderNodeMutation = new(
        id: "BESG005",
        title: "Render node changes state its Process reads without marking the node changed",
        messageFormat:
            "'{0}.{1}' changes '{2}', which '{0}.Process' reads, and no MarkChanged() call on this node is "
            + "reachable from it. A render graph recorded for a node that reports no changes may be reused "
            + "instead of re-recorded, so this change would never reach a frame. {3}. This rule reads an "
            + "assignment written inside '{0}' or a type it inherits from - to the state itself, to an "
            + "element of it, or through a call that changes it in place - and the "
            + "declaration of an auto-property, a field-like event or a field that code outside '{0}' can "
            + "write, so it staying silent is not proof that every mutation is marked.",
        category: "Beutl.Engine.SourceGenerators",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "Deciding in general whether a render node's recorded output can go stale is not possible, and "
            + "this rule does not try. It reports two shapes, both about state the node's Process reads. "
            + "The first is a change: a member of the node's own type, or of a type it inherits "
            + "from below RenderNode, assigns an instance field, an "
            + "auto-property, the backing store a property names with the field keyword, or an element of "
            + "one of those - _points[0] = value, which changes exactly the value Process reads back - or "
            + "calls a member on one of those that changes it in place, _items.Add(x), read by the name "
            + "of the call against a curated set of the in-place mutators the collection types are "
            + "written with, and skipped where the call cannot write its receiver at all - a static "
            + "method, a readonly member, a member of a readonly struct, or a member of string - and "
            + "neither that member nor a method of the same type it calls on this node marks that node "
            + "with MarkChanged(), and something outside the node - a public, internal or protected "
            + "internal member, or an explicit interface implementation - can reach that member without "
            + "marking either, because a write no caller can run unmarked leaves nothing stale. The "
            + "second is a declaration, because an assignment is not always written "
            + "where the rule can read it: a member the node's own type declares that code outside it can "
            + "write - an auto-property whose setter is neither private nor init-only, a field-like event "
            + "that is not private, or a field that is neither private nor readonly - has no body for the "
            + "first shape to read and no assignment inside the type for it to find, because the write is "
            + "made by whoever holds the node. The fix for it is the declaration - give the setter or the "
            + "event accessors a body that marks, or narrow the member to private, init-only or readonly "
            + "so that only the node's own code, which the first shape does read, can write it. The "
            + "Process both shapes read is the "
            + "override filling the RenderNode slot, inherited or not, and not a same-named overload "
            + "declared beside it. Everything else stays "
            + "invisible - an assignment made through a helper on another type or through a virtual call, "
            + "or through a ref parameter rebound onto the node's state; "
            + "an in-place change made by a call this rule does not know the name of, because that set is "
            + "a list of names and not a proof, so a mutating Bump is missed and a pure Add of one's own "
            + "type is reported; a write past the "
            + "name to a member of whatever the state holds, such as _child.Bounds = value, which is "
            + "another object's state and not this node's; an assignment guarded by a branch that skips "
            + "the MarkChanged() call the rule found elsewhere in the same member; an assignment in a "
            + "member that names MarkChanged without ever invoking it, because the suppression is by "
            + "symbol and not by invocation - which is what lets a method group handed to a scheduler or "
            + "stored in a delegate count - so naming it clears the member as much as calling it does; "
            + "an assignment in a member that only a marking member of this node reaches, which is how a "
            + "helper that reports what it changed and leaves the marking to its caller is written, and "
            + "which is judged for this node alone - a derived type reaching the same helper unmarked is "
            + "reported where that type is analyzed; and any assignment "
            + "written inside Process itself or a method Process calls, which are excluded so that ordinary "
            + "memoization is not reported. The "
            + "runtime cross-check in Beutl.Engine, not this rule, is what covers those. Treat silence as "
            + "the absence of the shape authors usually write, not as a proof that the node is safe to skip.");
}
