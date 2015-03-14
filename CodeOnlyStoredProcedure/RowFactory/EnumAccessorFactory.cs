﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using CodeOnlyStoredProcedure.DataTransformation;

namespace CodeOnlyStoredProcedure.RowFactory
{
    internal class EnumAccessorFactory<T> : AccessorFactoryBase
    {
        private static readonly Lazy<MethodInfo>    getStringMethod       = new Lazy<MethodInfo>(() => typeof(IDataRecord).GetMethod("GetString"));
        private        readonly ParameterExpression boxedValueExpression  = Expression.Variable (typeof(object));
        private        readonly ParameterExpression stringValueExpression = Expression.Variable (typeof(string));
        private        readonly ParameterExpression dataReaderExpression;
        private        readonly Expression          indexExpression;
        private        readonly Expression          boxedExpression;
        private        readonly Expression          unboxedExpression;
        private        readonly Expression          parseStringExpression;
        private        readonly Expression          attributeExpression;
        private        readonly string              errorMessage;
        private        readonly string              propertyName;
        private        readonly bool                convertNumeric;
        private        readonly IEnumerable<DataTransformerAttributeBase> transformers = Enumerable.Empty<DataTransformerAttributeBase>();

        public EnumAccessorFactory(ParameterExpression dataReaderExpression, Expression index, PropertyInfo propertyInfo, string columnName)
        {
            this.indexExpression      = index;
            this.dataReaderExpression = dataReaderExpression;

            var type       = typeof(T);
            var dbType     = type;
            var isNullable = GetUnderlyingNullableType(ref dbType);

            if (!dbType.IsEnum)
                throw new NotSupportedException("Can not use an EnumRowFactory on a type that is not an Enum.");

            boxedExpression = CreateBoxedRetrieval(dataReaderExpression,
                                                   index,
                                                   boxedValueExpression,
                                                   propertyInfo,
                                                   columnName,
                                                   out errorMessage);

            if (propertyInfo == null)
            {
                propertyName = "result";
                errorMessage = "Null value is not allowed for single column result set that returns " +
                                typeof(T) + ", but null was the result from the stored procedure.";
                attributeExpression = Expression.Constant(null, typeof(Attribute[]));
            }
            else
            {
                propertyName = propertyInfo.Name;
                var attrs = propertyInfo.GetCustomAttributes(false).Cast<Attribute>().ToArray();
                transformers = attrs.OfType<DataTransformerAttributeBase>()
                                    .OrderBy(x => x.Order)
                                    .ToArray();
                attributeExpression = Expression.Constant(attrs);
                convertNumeric = attrs.OfType<ConvertNumericAttribute>().Any();
            }

            unboxedExpression = CreateUnboxedRetrieval<T>(dataReaderExpression, index, transformers);

            var underlying = dbType;
            GetUnderlyingEnumType(ref underlying);

            var names    = Enum.GetNames (dbType);
            var values   = Enum.GetValues(dbType);
            var cases    = new List<SwitchCase>();
            var defCases = new List<SwitchCase>();
            var res      = Expression.Variable(underlying, "res");
            var strings  = Expression.Variable(typeof(string[]), "strings");
            var idx      = Expression.Variable(typeof(int), "i");

            for (int i = 0; i < names.Length; ++i)
            {
                var n   = Expression.Constant(names[i]);
                var val = Expression.Constant(Convert.ChangeType(values.GetValue(i), underlying));

                cases   .Add(Expression.SwitchCase(Expression.Assign  (res, val), n));
                defCases.Add(Expression.SwitchCase(Expression.OrAssign(res, val), n));
            }

            var str     = Expression.Variable(typeof(string), "str");
            var endLoop = Expression.Label("endLoop");
            var excep = Expression.Block( Expression.Throw(
                                Expression.New(typeof(NotSupportedException).GetConstructor(new[] { typeof(string) }),
                                    Expression.Call(typeof(string).GetMethod("Concat", new[] { typeof(string), typeof(string), typeof(string) }),
                                        Expression.Constant("Could not parse the string \""), 
                                        str, 
                                        Expression.Constant("\" into an enum of type " + dbType + ".")
                                    )
                                )
                            ), res);
            var defExpr = Expression.Block(
                new[] { idx, strings, str },
                Expression.Assign(res, Expression.Constant(Convert.ChangeType(0, underlying))),
                Expression.Assign(strings, Expression.Call(stringValueExpression, typeof(string).GetMethod("Split", new[] { typeof(char[]) }), Expression.Constant(new char[] { ',' }))),
                Expression.Assign(idx, Expression.Constant(0)),
                Expression.Loop(
                    Expression.Block(
                        Expression.Assign(str, Expression.Call(Expression.ArrayIndex(strings, idx), typeof(string).GetMethod("Trim", new Type[0]))),
                        Expression.Switch(
                            str, 
                            excep,
                            defCases.ToArray()
                        ),
                        Expression.PreIncrementAssign(idx),
                        Expression.IfThen(Expression.GreaterThanOrEqual(idx, Expression.ArrayLength(strings)), Expression.Break(endLoop))
                    )
                ),
                Expression.Label(endLoop),
                res
            );

            parseStringExpression = Expression.Block(
                dbType,
                new[] { res },
                Expression.Switch(
                    stringValueExpression,
                    defExpr,
                    cases.ToArray()
                ),
                Expression.Convert(res, dbType)
            );
            
            if (isNullable)
                parseStringExpression = Expression.Convert(parseStringExpression, type);
        }

        public override Expression CreateExpressionToGetValueFromReader(IDataReader reader, IEnumerable<IDataTransformer> xFormers, Type dbColumnType)
        {
            var        type         = typeof(T);
            Expression body         = null;
            var        unnulledType = type;
            var        isNullable   = GetUnderlyingNullableType(ref unnulledType);
            var        expectedType = unnulledType;
            GetUnderlyingEnumType(ref expectedType);
            var        underlying   = expectedType;
            StripSignForDatabase(ref expectedType);

            if (dbColumnType == typeof(string))
            {
                var exprs = new List<Expression>();

                var getString = Expression.Call(
                    dataReaderExpression,
                    getStringMethod.Value,
                    indexExpression
                );

                Expression nullValueExpression;
                if (isNullable || xFormers.Any())
                    nullValueExpression = Expression.Assign(stringValueExpression, Expression.Constant(null, typeof(string)));
                else
                {
                    nullValueExpression = Expression.Throw(
                        Expression.New(
                            typeof(NoNullAllowedException).GetConstructor(new[] { typeof(string) }),
                            Expression.Constant(errorMessage)
                        )
                    );
                }

                exprs.Add(
                    Expression.IfThenElse(
                        Expression.Call(dataReaderExpression, IsDbNullMethod, indexExpression),
                        nullValueExpression,
                        Expression.Assign(stringValueExpression, getString)
                    )
                );

                if (xFormers.Any())
                {
                    exprs.Add(Expression.Assign(boxedValueExpression, stringValueExpression));
                    AddTransformers(type, boxedValueExpression, Expression.Constant(null, typeof(Attribute[])), xFormers, exprs);
                    exprs.Add(Expression.Assign(stringValueExpression, Expression.Convert(boxedValueExpression, typeof(string))));
                }

                if (isNullable)
                {
                    exprs.Add(
                        Expression.Condition(
                            Expression.ReferenceEqual(stringValueExpression, Expression.Constant(null, typeof(string))),
                            Expression.Constant(null, type),
                            parseStringExpression
                        )
                    );
                }
                else
                {
                    exprs.Add(
                        Expression.IfThen(
                            Expression.ReferenceEqual(stringValueExpression, Expression.Constant(null, typeof(string))),
                            Expression.Throw(
                                Expression.New(
                                    typeof(NoNullAllowedException).GetConstructor(new[] { typeof(string) }),
                                    Expression.Constant(errorMessage)
                                )
                            )
                        )
                    );
                    exprs.Add(parseStringExpression);
                }

                body = Expression.Block(type, new[] { boxedValueExpression, stringValueExpression }, exprs);
            }
            else if (xFormers.Any() || unboxedExpression == null)
            {
                var exprs = new List<Expression>();

                exprs.Add(boxedExpression);

                Expression res;
                if (xFormers.Any())
                {
                    AddTransformers(type, boxedValueExpression, attributeExpression, xFormers, exprs);
                    res = CreateUnboxingExpression(unnulledType, isNullable, boxedValueExpression, exprs, errorMessage);
                }
                else
                {
                    res = CreateUnboxingExpression(dbColumnType, isNullable, boxedValueExpression, exprs, errorMessage);

                    if (dbColumnType != expectedType)
                    {
                        if (convertNumeric)
                            res = Expression.Convert(res, expectedType);
                        else
                            throw new StoredProcedureColumnException(expectedType, dbColumnType, propertyName);
                    }

                    if (expectedType != underlying)
                        res = Expression.Convert(res, underlying);
                }

                exprs.Add(Expression.Convert(res, type));

                body = Expression.Block(type, new[] { boxedValueExpression }, exprs);
            }
            else if (dbColumnType != expectedType)
            {
                if (convertNumeric)
                    body = CreateUnboxedRetrieval<T>(dataReaderExpression, indexExpression, transformers, dbColumnType);
                else
                    throw new StoredProcedureColumnException(type, dbColumnType, propertyName);
            }
            else
                body = unboxedExpression;

            return body;
        }
    }
}