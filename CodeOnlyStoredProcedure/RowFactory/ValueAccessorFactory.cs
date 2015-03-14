﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using CodeOnlyStoredProcedure.DataTransformation;

namespace CodeOnlyStoredProcedure.RowFactory
{
    internal class ValueAccessorFactory<T> : AccessorFactoryBase
    {
        private readonly IEnumerable<DataTransformerAttributeBase> transformers = Enumerable.Empty<DataTransformerAttributeBase>();
        private readonly ParameterExpression boxedValueExpression = Expression.Variable(typeof(object), "value");
        private readonly ParameterExpression dataReaderExpression;
        private readonly Expression          indexExpression;
        private readonly Expression          boxedExpression;
        private readonly Expression          unboxedExpression;
        private readonly Expression          attributeExpression;
        private readonly string              errorMessage;
        private readonly string              propertyName;
        private readonly bool                convertNumeric;

        public ValueAccessorFactory(ParameterExpression dataReaderExpression, Expression index, PropertyInfo propertyInfo, string columnName)
        {
            this.dataReaderExpression = dataReaderExpression;
            this.indexExpression      = index;
            
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
        }

        public override Expression CreateExpressionToGetValueFromReader(IDataReader reader, IEnumerable<IDataTransformer> xFormers, Type dbColumnType)
        {
            var        type         = typeof(T);
            Expression body         = null;
            var        expectedType = type;
            var        isNullable   = GetUnderlyingNullableType(ref expectedType);
            StripSignForDatabase(ref expectedType);

            if (xFormers.Any() || unboxedExpression == null)
            {
                var exprs = new List<Expression>();

                exprs.Add(boxedExpression);

                if (xFormers.Any())
                    AddTransformers(type, boxedValueExpression, attributeExpression, xFormers, exprs);

                var res = CreateUnboxingExpression(dbColumnType, isNullable, boxedValueExpression, exprs, errorMessage);

                if (dbColumnType != expectedType)
                {
                    if (convertNumeric)
                        res = Expression.Convert(res, type);
                    else
                        throw new StoredProcedureColumnException(type, dbColumnType, propertyName);
                }
                else if (isNullable)
                    res = Expression.Convert(res, type);

                exprs.Add(res);

                body = Expression.Block(type, new[] { boxedValueExpression }, exprs);
            }
            else
            {
                if (dbColumnType != expectedType)
                {
                    if (convertNumeric)
                        body = CreateUnboxedRetrieval<T>(dataReaderExpression, indexExpression, transformers, dbColumnType);
                    else
                        throw new StoredProcedureColumnException(type, dbColumnType, propertyName);
                }
                else
                    body = unboxedExpression;
            }

            return body;
        }
    }
}