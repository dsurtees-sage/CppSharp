using CppSharp.AST;
using System.Linq;

namespace CppSharp.Passes
{
    public class ExtractInterfacePass : InterfaceExtractor
    {
        /// <summary>
        /// Creates interface from generated classes
        /// </summary>
        /// 
        public override bool VisitASTContext(ASTContext context)
        {
            return base.VisitASTContext(context);
        }

        public override bool VisitClassDecl(Class @class)
        {
            if(@class.Name == "CBInvoicePaymentsProvider")
            {
                int i = 0;
                i++;
            }
            if (!@class.IsGenerated || AlreadyVisited(@class))
                return false;

            if (@class.IsInterface || (Options.IsCLIGenerator && (@class.IsOpaque || @class.IsStatic || @class.IsValueType)))
            {
                return false;
            }

            GetInterface(@class);
            return true;
        }
    }
}
