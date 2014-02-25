using System;
using System.CodeDom;
using System.CodeDom.Compiler;
using System.IO;
using System.Reflection;

namespace SdkGenerator
{
    public class EnumCreator
    {
        private CodeNamespace _codeNamespace;
        private CodeTypeDeclaration _targetClass;
        private CodeCompileUnit _targetUnit;

        public EnumCreator(string nameSpace)
        {
            _codeNamespace = new CodeNamespace(nameSpace);
        }

        public EnumCreator SetClassName(string className, bool useTypeT)
        {
            var _className = className;
            _targetUnit = new CodeCompileUnit();
            _targetClass = new CodeTypeDeclaration(className)
            {
                IsEnum = true,
                TypeAttributes = TypeAttributes.Public
            };

            if (useTypeT)
            {
                _targetClass.TypeParameters.Add(new CodeTypeParameter("T"));
            }

            _codeNamespace.Types.Add(_targetClass);
            _targetUnit.Namespaces.Add(_codeNamespace);

            return this;
        }

        public EnumCreator AddEnumMembersFromType(Type enumType)
        {
            var values = Enum.GetValues(enumType);

            for (var i = 0; i < values.Length; i++)
            {
                var value = (int)values.GetValue(i);
                var name = Enum.GetName(enumType, value);
                AddMember(name, value);
            }


            return this;
        }

        private void AddMember(string memberName, object value)
        {
            var member = new CodeMemberField
            {
                Name = memberName,
                InitExpression = new CodePrimitiveExpression(value) // Uses key for value
            };

            _targetClass.Members.Add(member);
        }

        public void GenerateCSharpCode(string fileName)
        {
            var provider = CodeDomProvider.CreateProvider("CSharp");
            var options = new CodeGeneratorOptions
            {
                BracingStyle = "C"
            };

            using (var sourceWriter = new StreamWriter(fileName))
            {
                provider.GenerateCodeFromCompileUnit(_targetUnit, sourceWriter, options);
            }
        }
    }
}