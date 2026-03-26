using CppSharp.AST;
using CppSharp.Generators;
using System.Collections.Generic;
using System.Linq;

namespace CppSharp.Passes
{
    public class InterfaceExtractor : TranslationUnitPass
    {
        /// <summary>
        /// Collects all interfaces in a unit to be added at the end 
        /// because the unit cannot be changed while it's being iterated though.
        /// We also need it to check if a class already has a complementary interface
        /// because different classes may have the same secondary bases.
        /// </summary>
        protected readonly Dictionary<Class, HashSet<Class>> _interfacesGenerated = new Dictionary<Class, HashSet<Class>>();

        /// <summary>
        /// Creates interface from generated classes
        /// </summary>
        /// 
        public override bool VisitASTContext(ASTContext context)
        {
            bool result = base.VisitASTContext(context);

            OnVisitASTContext(context);

            return result;
        }

        protected virtual bool OnVisitASTContext(ASTContext context)
        {
            foreach (var @interface in _interfacesGenerated
                .SelectMany(ig => ig.Value.Where(i => !(i is ClassTemplateSpecialization))))
            {
                if (!@interface.Namespace.Declarations.Contains(@interface))
                {
                    int index = @interface.Namespace.Declarations.IndexOf(@interface.OriginalClass);
                    @interface.Namespace.Declarations.Insert(index, @interface);
                }
            }

            foreach (var mapping in _interfacesGenerated)
            {
                var @class = mapping.Key;
                var interfaces = mapping.Value;

                foreach (var @interface in interfaces)
                {
                    ImplementInterfaceMethods(@class, @interface);
                    ImplementInterfaceProperties(@class, @interface);
                }
            }

            return true;
        }

        public override bool VisitClassDecl(Class @class)
        {
            return base.VisitClassDecl(@class);
        }

        public Class GetInterface(Class @base, Class target = null)
        {
            target = target ?? @base;

            if (@base.CompleteDeclaration != null)
                @base = (Class)@base.CompleteDeclaration;

            //see if we have an existing interface first
            var existingInterface = @base.Bases.FirstOrDefault(b => b.Class.OriginalClass == @base)?.Class;

            if (existingInterface != null)
                InsertInterface(target, existingInterface);

            return existingInterface ??
                _interfacesGenerated.SelectMany(ig => ig.Value).FirstOrDefault(i => i.OriginalClass == @base) ??
                GetNewInterface("I" + @base.Name, @base);
        }

        private Class GetNewInterface(string name, Class @base)
        {
            var specialization = @base as ClassTemplateSpecialization;
            Class @interface;
            if (specialization == null)
            {
                @interface = new Class();
            }
            else
            {
                Class template = specialization.TemplatedDecl.TemplatedClass;
                Class templatedInterface = GetInterface(template);
                @interface = _interfacesGenerated.SelectMany(ig => ig.Value).FirstOrDefault(i => i.OriginalClass == @base);
                if (@interface != null)
                    return @interface;
                var specializedInterface = new ClassTemplateSpecialization();
                specializedInterface.Arguments.AddRange(specialization.Arguments);
                specializedInterface.TemplatedDecl = new ClassTemplate { TemplatedDecl = templatedInterface };
                @interface = specializedInterface;
            }
            @interface.Name = name;
            @interface.USR = @base.USR;
            @interface.Namespace = @base.Namespace;
            @interface.OriginalNamespace = @base.OriginalNamespace;
            @interface.Access = @base.Access;
            @interface.Type = ClassType.Interface;
            @interface.OriginalName = @base.OriginalName;
            @interface.OriginalClass = @base;

            @interface.Methods.AddRange(
                from m in @base.Methods
                where !m.IsConstructor && !m.IsDestructor && !m.IsStatic &&
                    (m.IsGenerated || m.IsInvalid && specialization != null) && !m.IsOperator
                    && (Options.GeneratorKind == GeneratorKind.CLI ? MarkAsOverride(m) : true)
                select new Method(m) { Namespace = @interface, OriginalFunction = m });

            @interface.Bases.AddRange(
                from b in @base.Bases
                where b.Class != null && b.Class.IsGenerated
                let i = b.Class.IsInterface ? b.Class : GetInterface(b.Class)
                select new BaseClassSpecifier(b) { Type = new TagType(i) });

            @interface.Properties.AddRange(
                from property in @base.Properties
                where property.IsDeclared
                && (Options.GeneratorKind == GeneratorKind.CLI ? MarkAsOverride(property) : true)
                select CreateInterfaceProperty(property, @interface));

            @interface.Fields.AddRange(@base.Fields);

            foreach (var @declaration in @base.Declarations)
            {
                @declaration.Namespace = @interface;
                @declaration.OriginalNamespace = @base;
            }

            // avoid conflicts when potentially renaming later
            @interface.Declarations.AddRange(@base.Declarations);

            @base.Declarations.Clear();

            if (@interface.Bases.Count == 0)
            {
                QualifiedType intPtr = new QualifiedType(
                    new BuiltinType(PrimitiveType.IntPtr));

                var instance = new Property
                {
                    Namespace = @interface,
                    Name = Helpers.InstanceIdentifier,
                    QualifiedType = intPtr,
                    GetMethod = new Method
                    {
                        Name = Helpers.InstanceIdentifier,
                        SynthKind = FunctionSynthKind.InterfaceInstance,
                        Namespace = @interface,
                        OriginalReturnType = intPtr
                    }
                };

                @interface.Properties.Add(instance);

                if (Options.GeneratorKind == GeneratorKind.CSharp)
                {
                    var dispose = new Method
                    {
                        Namespace = @interface,
                        Name = "Dispose",
                        ReturnType = new QualifiedType(new BuiltinType(PrimitiveType.Void)),
                        SynthKind = FunctionSynthKind.InterfaceDispose,
                        Mangled = string.Empty
                    };

                    @interface.Methods.Add(dispose);
                }
            }

            if (Options.GeneratorKind == GeneratorKind.CSharp)
            {
                @interface.Declarations.AddRange(@base.Events);

                var type = new QualifiedType(new BuiltinType(PrimitiveType.IntPtr));
                string pointerAdjustment = "__PointerTo" + @base.Name;
                var adjustmentTo = new Property
                {
                    Namespace = @interface,
                    Name = pointerAdjustment,
                    QualifiedType = type,
                    GetMethod = new Method
                    {
                        Name = pointerAdjustment,
                        SynthKind = FunctionSynthKind.InterfaceInstance,
                        Namespace = @interface,
                        ReturnType = type
                    }
                };
                @interface.Properties.Add(adjustmentTo);
                @base.Properties.Add(adjustmentTo);
            }

            @base.Bases.Add(new BaseClassSpecifier { Type = new TagType(@interface) });

            InsertInterface(@base, @interface);

            if (@base.IsTemplate)
            {
                @interface.IsDependent = true;
                @interface.TemplateParameters.AddRange(@base.TemplateParameters);
                templatedInterfaces[@base] = @interface;
                foreach (var spec in @base.Specializations)
                    @interface.Specializations.Add(
                        (ClassTemplateSpecialization)GetNewInterface(name, spec));
            }
            return @interface;
        }

        protected static Property CreateInterfaceProperty(Property property, DeclarationContext @namespace)
        {
            var interfaceProperty = new Property(property) { Namespace = @namespace };
            if (property.GetMethod != null)
            {
                interfaceProperty.GetMethod = new Method(property.GetMethod)
                {
                    OriginalFunction = property.GetMethod,
                    Namespace = @namespace
                };
            }

            if (property.SetMethod != null)
            {
                // handle indexers
                interfaceProperty.SetMethod = property.GetMethod == property.SetMethod ?
                    interfaceProperty.GetMethod : new Method(property.SetMethod)
                    {
                        OriginalFunction = property.SetMethod,
                        Namespace = @namespace
                    };
            }

            return interfaceProperty;
        }

        private bool MarkAsOverride(Method m)
        {
            m.IsOverride = true;

            return true;
        }

        private bool MarkAsOverride(Property p)
        {
            if (p.HasGetter)
                p.GetMethod.IsOverride = true;

            if (p.HasSetter)
                p.SetMethod.IsOverride = true;

            return true;
        }


        protected virtual void ImplementInterfaceMethods(Class @class, Class @interface)
        {  
            //Enumerate all base classes so we don't implement any interface methods that are already implemented
            //by a base class
            var allBases = new HashSet<Class>();
            var currentLayer = new HashSet<Class> { @class };
            while(true)
            {
                var bases = currentLayer.SelectMany(b => b.Bases.Where(bc => !bc.Class.IsInterface).Select(b => b.Class)).ToList();
                if (bases.Count() == 0)
                    break;

                currentLayer.Clear();
                currentLayer.UnionWith(bases);
                allBases.UnionWith(bases);
            }

            //Dont need to implement any methods that are implemented by a base class
            if (allBases.Any(b => b == @interface.OriginalClass))
                return;

            foreach (var method in @interface.Methods.Where(
                m => m.SynthKind != FunctionSynthKind.InterfaceDispose))
            {
                var existingImpl = @class.Methods.Find(
                    m => m.OriginalName == method.OriginalName &&
                        m.Parameters.Where(p => !p.Ignore).SequenceEqual(
                            method.Parameters.Where(p => !p.Ignore),
                            ParameterTypeComparer.Instance));

                if (existingImpl != null)
                {
                    if (existingImpl.OriginalFunction == null)
                        existingImpl.OriginalFunction = method;

                    continue;
                }

                var impl = new Method(method)
                {
                    Namespace = @class,
                    OriginalNamespace = @interface,
                    OriginalFunction = method.OriginalFunction
                };

                var rootBaseMethod = @class.GetBaseMethod(method);
                if (rootBaseMethod != null && rootBaseMethod.IsDeclared)
                    impl.ExplicitInterfaceImpl = @interface;

                @class.Methods.Add(impl);
            }
        }

        protected void ImplementInterfaceProperties(Class @class, Class @interface)
        {
            foreach (var property in @interface.Properties.Where(p => p.Name != Helpers.InstanceIdentifier))
            {
                var impl = CreateInterfaceProperty(property, @class);
                impl.OriginalNamespace = @interface;

                var rootBaseProperty = @class.GetBasePropertyByName(property, true);
                if (rootBaseProperty != null && rootBaseProperty.IsDeclared)
                    impl.ExplicitInterfaceImpl = @interface;

                @class.Properties.Add(impl);
            }

            foreach (var @base in @interface.Bases)
                ImplementInterfaceProperties(@class, @base.Class);
        }

        private void InsertInterface(Class @class, Class @interface)
        {
            if (!_interfacesGenerated.ContainsKey(@class))
                _interfacesGenerated.Add(@class, new HashSet<Class>());

            _interfacesGenerated[@class].Add(@interface);
        }

        private readonly Dictionary<Class, Class> templatedInterfaces = new Dictionary<Class, Class>();
    }
}
