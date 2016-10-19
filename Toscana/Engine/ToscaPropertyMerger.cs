﻿using System.Collections.Generic;
using System.Linq;

namespace Toscana.Engine
{
    internal interface IToscaPropertyMerger
    {
        IReadOnlyDictionary<string, ToscaPropertyDefinition> CombineAndMerge<T>(IToscaEntityWithProperties<T> toscaEntity)
            where T : IToscaEntityWithProperties<T>;
    }

    internal class ToscaPropertyMerger : IToscaPropertyMerger
    {
        private readonly IToscaPropertyCombiner toscaPropertyCombiner;

        public ToscaPropertyMerger(IToscaPropertyCombiner toscaPropertyCombiner)
        {
            this.toscaPropertyCombiner = toscaPropertyCombiner;
        }

        public IReadOnlyDictionary<string, ToscaPropertyDefinition> CombineAndMerge<T>(IToscaEntityWithProperties<T> toscaEntity)
            where T : IToscaEntityWithProperties<T>
        {
            var combineProperties = toscaPropertyCombiner.CombineProperties(toscaEntity);
            return MergeProperties(combineProperties);
        }

        private Dictionary<string, ToscaPropertyDefinition> MergeProperties(Dictionary<string, List<ToscaPropertyDefinition>> combinedProperties)
        {
            var mergedProperties = new Dictionary<string, ToscaPropertyDefinition>();
            foreach (var property in combinedProperties)
            {
                var mergedProperty = property.Value.Last();
                if (property.Value.Count > 1)
                {
                    for (int i = property.Value.Count - 2; i >= 0; i--)
                    {
                        var overridingProperty = property.Value[i];
                        MergeProperty(overridingProperty, mergedProperty);
                    }
                }
                mergedProperties.Add(property.Key, mergedProperty);
            }
            return mergedProperties;
        }

        private static void MergeProperty(ToscaPropertyDefinition overridingProperty, ToscaPropertyDefinition mergedProperty)
        {
            if (overridingProperty.Default != null)
            {
                mergedProperty.Default = overridingProperty.Default;
            }
            if (overridingProperty.Description != null)
            {
                mergedProperty.Description = overridingProperty.Description;
            }
            if (overridingProperty.Constraints != null && overridingProperty.Constraints.Any())
            {
                var mergedConstraints = mergedProperty.GetConstraintsDictionary();
                var overringConstraints = overridingProperty.GetConstraintsDictionary();
                foreach (var overringConstraint in overringConstraints)
                {
                    mergedConstraints[overringConstraint.Key] = overringConstraint.Value;
                }
                mergedProperty.SetConstraints(mergedConstraints);
            }
            mergedProperty.Required = overridingProperty.Required;
        }
    }
}