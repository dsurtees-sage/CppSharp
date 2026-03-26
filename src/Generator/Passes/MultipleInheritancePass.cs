using CppSharp.AST;
using System.Linq;

namespace CppSharp.Passes
{
    public class MultipleInheritancePass : InterfaceExtractor
    {        
        public MultipleInheritancePass()
            => VisitOptions.ResetFlags(VisitFlags.Default);

        public override bool VisitASTContext(ASTContext context)
        {
            return base.VisitASTContext(context);
        }

        protected override bool OnVisitASTContext(ASTContext context)
        {
            foreach (var mapping in _interfacesGenerated)
            {
                var @class = mapping.Key;
                var interfaces = mapping.Value;

                for (var i = 1; i < @class.Bases.Count; i++)
                {
                    var @base = @class.Bases[i];
                    Class @interface = interfaces.FirstOrDefault(iface => iface.OriginalClass == @base.Class);
                    if (@interface == null)
                        continue;
                    @class.Bases[i] = new BaseClassSpecifier(@base) { Type = new TagType(@interface) };
                }
            }

            return base.OnVisitASTContext(context);
        }

        public override bool VisitClassDecl(Class @class)
        {
            //We only care about non interface base classes
            var bases = @class.Bases.Where(b => !b.Class.IsInterface);

            //1 or fewer base classes is fine, we only care about classes with more than 1 concrete base class
            if (!base.VisitClassDecl(@class) || !@class.IsGenerated || bases.Count() <= 1)
                return false;

            //Skip the first element as we only care to flatten base classes beyond the first
            foreach (var baseClass in bases.Select(b => b.Class).Skip(1))
            {
                if (baseClass == null || baseClass.IsInterface || !baseClass.IsGenerated) continue;
                GetInterface(baseClass, @class);
            }
            return true;
        }

        protected override void ImplementInterfaceMethods(Class @class, Class @interface)
        {
            if(@class.Name == "CBBackupRestore")
            {
                int i = 0;
                i++;
            }    

            base.ImplementInterfaceMethods(@class, @interface);

            foreach (var @base in @interface.Bases.Where(b => b.Class.IsInterface))
                ImplementInterfaceMethods(@class, @base.Class);
        }
    }
}
