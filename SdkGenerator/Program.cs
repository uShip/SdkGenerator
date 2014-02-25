using System;
using System.CodeDom;
using System.CodeDom.Compiler;
using System.IO;
using System.Reflection;

namespace SdkGenerator
{
    internal class Program
    {
        private static void Main()
        {
            //test
            var asm = Assembly.LoadFrom(@"C:\inetpub\wwwroot\uShipTrunk3\uShip.Api.Common\bin\uShip.Api.Common.dll");

            var type = asm.GetType("uShip.Api.Common.Outputs.Marketplace.ListingModel");

            WriteClassProperties(type);
        }

        public static void WriteClassProperties(Type classType)
        {
            var className = classType.Name.Replace("`1", "");

            if (!File.Exists(@"c:\temp\data\" + className + ".cs"))

            {
                var creator = new ClassCreator(classType.Namespace);

                if (classType.GetGenericArguments().Length > 0)
                {
                    foreach (var genericArgument in classType.GetGenericArguments())
                    {
                        if (!genericArgument.IsEnum)
                        {
                            WriteClassProperties(genericArgument);
                        }

                        if (genericArgument.IsEnum)
                        {
                            WriteEnum(genericArgument);
                        }
                    }
                }

                var useTypeT = classType.IsConstructedGenericType;

                creator.SetClassName(className, useTypeT);

                foreach (var prop in classType.GetProperties())
                {
                    if (prop.PropertyType.IsGenericType)
                    {
                        foreach (var t in prop.PropertyType.GetGenericArguments())
                        {
                            if (t.IsClass && !t.FullName.StartsWith("System"))
                            {
                                WriteClassProperties(t);
                            }

                            if (t.IsEnum && !t.FullName.StartsWith("System"))
                            {
                                WriteEnum(t);
                            }
                        }

                        creator.AddProperties(prop.Name, prop.PropertyType);
                    }
                    else
                    {
                        if (prop.PropertyType.IsClass && !prop.PropertyType.FullName.StartsWith("System"))
                        {
                            WriteClassProperties(prop.PropertyType);
                        }

                        creator.AddProperties(prop.Name, prop.PropertyType);
                    }
                }

                creator.GenerateCSharpCode(@"c:\temp\data\" + className + ".cs");
            }
        }

        private static void WriteEnum(Type genericArgument)
        {
            var enumName = genericArgument.Name.Replace("`1", "");
            var enumDestination = @"c:\temp\data\" + enumName + ".cs";
            if (!File.Exists(enumDestination))
            {
                new EnumCreator(genericArgument.Namespace).SetClassName(genericArgument.Name, false).AddEnumMembersFromType(genericArgument).GenerateCSharpCode(enumDestination);
            }
        }
    }


    public class EnumCreator
    {
        private string _className;
        private readonly CodeNamespace _codeNamespace;
        private CodeTypeDeclaration _targetClass;
        private CodeCompileUnit _targetUnit;

        public EnumCreator(string nameSpace)
        {
            _codeNamespace = new CodeNamespace(nameSpace);
        }

        public EnumCreator SetClassName(string className, bool useTypeT)
        {
            _className = className;
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

            for (int i = 0; i < values.Length; i++)
            {
                var value = (int) values.GetValue(i);
                var name = Enum.GetName(enumType, value);
                AddMember(name,value);
            }

//            
//            foreach (var member in enumType.GetMembers(BindingFlags.Public | BindingFlags.Static))
//            {
//                var name = member.Name;
//                var value = member.GetType().Name;
//                
//                AddMember(member.Name);
//            }

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

    public class ClassCreator
    {
        private readonly CodeNamespace _codeNamespace;
        private CodeTypeDeclaration _targetClass;
        private CodeCompileUnit _targetUnit;

        public ClassCreator(string nameSpace)
        {
            _codeNamespace = new CodeNamespace(nameSpace);
        }

        public ClassCreator SetClassName(string className, bool useTypeT)
        {
            _targetUnit = new CodeCompileUnit();
            _targetClass = new CodeTypeDeclaration(className)
            {
                IsClass = true,
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

        public ClassCreator AddImports(string importName)
        {
            _codeNamespace.Imports.Add(new CodeNamespaceImport(importName));

            return this;
        }

        public ClassCreator GenerateCSharpCode(string fileName)
        {
            var provider = CodeDomProvider.CreateProvider("CSharp");
            var options = new CodeGeneratorOptions {BracingStyle = "C"};
            using (var sourceWriter = new StreamWriter(fileName))
            {
                provider.GenerateCodeFromCompileUnit(_targetUnit, sourceWriter, options);
            }

            return this;
        }


        public ClassCreator AddProperties(string propertyName, Type propertyType)
        {
            var backingField = new CodeMemberField(propertyType, "_" + propertyName);
            _targetClass.Members.Add(backingField);

            // Declare the read-only Width property.
            var memberProperty = new CodeMemberProperty
            {
                Attributes = MemberAttributes.Public | MemberAttributes.Final,
                Name = propertyName,
                HasGet = true,
                HasSet = true,
                Type = new CodeTypeReference(propertyType)
            };

            memberProperty.GetStatements.Add(new CodeMethodReturnStatement(
                new CodeFieldReferenceExpression(
                    new CodeThisReferenceExpression(), "_" + propertyName)));

            memberProperty.SetStatements.Add(
                new CodeAssignStatement(new CodeFieldReferenceExpression(new CodeThisReferenceExpression(), "_" + propertyName),
                    new CodePropertySetValueReferenceExpression())
                );

            _targetClass.Members.Add(memberProperty);


            return this;
        }
    }
}