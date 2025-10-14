import React, { useState } from 'react';
import { AlertCircle, CheckCircle, ChevronDown, ChevronRight, Info, FileText, Database } from 'lucide-react';

// Sample data structure - replace with your actual data
const sampleData = {
Metadata: {
ComparisonDate: "2025-10-14T10:30:00Z",
SourceTenantId: "tenant-source-001",
TargetTenantId: "tenant-target-002",
Duration: "00:05:32.1234567"
},
Summary: {
TotalDifferences: 42,
AreIdentical: false,
CkModelDifferences: 5,
CkTypeDifferences: 12,
RtEntityDifferences: 20,
AssociationDifferences: 3,
MetadataDifferences: 2
},
MetadataComparison: {
Source: {
TenantId: "tenant-source-001",
DatabaseName: "source_db",
TotalRtEntityCount: 1250,
TotalAssociationCount: 450,
CkModelCount: 15
},
Target: {
TenantId: "tenant-target-002",
DatabaseName: "target_db",
TotalRtEntityCount: 1268,
TotalAssociationCount: 447,
CkModelCount: 18
},
Differences: [
{ FieldName: "TotalRtEntityCount", SourceValue: 1250, TargetValue: 1268, Description: "Entity count mismatch" }
]
}
};

const ComparisonViewer = () => {
// Load data from global window object (injected by C# generator)
const initialData = (window as any).COMPARISON_DATA || sampleData;
const [data, setData] = useState(initialData);
const [activeTab, setActiveTab] = useState('summary');
const [expandedSections, setExpandedSections] = useState({});
const [expandedEntities, setExpandedEntities] = useState({});

const toggleSection = (section) => {
setExpandedSections(prev => ({
...prev,
[section]: !prev[section]
}));
};

const toggleEntity = (entityKey) => {
setExpandedEntities(prev => ({
...prev,
[entityKey]: !prev[entityKey]
}));
};

const formatDate = (dateStr) => {
if (!dateStr) return 'N/A';
return new Date(dateStr).toLocaleString();
};

const formatValue = (value) => {
if (value === null || value === undefined) return 'null';
if (typeof value === 'object') return JSON.stringify(value, null, 2);
return String(value);
};

const SummaryCard = ({ title, value, color, icon: Icon }) => (
<div className="bg-white rounded-lg shadow p-6 border-l-4" style={{ borderLeftColor: color }}>
    <div className="flex items-center justify-between">
        <div>
            <p className="text-sm text-gray-600 mb-1">{title}</p>
            <p className="text-3xl font-bold" style={{ color }}>{value}</p>
        </div>
        {Icon && <Icon size={40} color={color} className="opacity-20" />}
    </div>
</div>
);

const CollapsibleSection = ({ title, count, children, colorClass = "bg-gray-50" }) => {
const sectionKey = title.replace(/\s+/g, '-');
const isExpanded = expandedSections[sectionKey];

return (
<div className="mb-4 border rounded-lg overflow-hidden">
    <button
            onClick={() => toggleSection(sectionKey)}
    className={`w-full px-4 py-3 flex items-center justify-between ${colorClass} hover:opacity-80 transition-opacity`}
    >
    <div className="flex items-center gap-2">
        {isExpanded ? <ChevronDown size={20} /> : <ChevronRight size={20} />}
        <span className="font-semibold">{title}</span>
        {count !== undefined && (
        <span className="bg-white px-2 py-1 rounded text-sm font-bold">{count}</span>
        )}
    </div>
    </button>
    {isExpanded && (
    <div className="p-4 bg-white">
        {children}
    </div>
    )}
</div>
);
};

const EntityCard = ({ entity, type = "source" }) => {
const bgColor = type === "source" ? "bg-blue-50" : "bg-green-50";
const borderColor = type === "source" ? "border-blue-200" : "border-green-200";

return (
<div className={`${bgColor} ${borderColor} border rounded p-3 text-sm`}>
    <div className="font-mono text-xs mb-2">
        <span className="font-semibold">RtId:</span> {formatValue(entity.RtId)}
    </div>
    {entity.CkTypeId && (
    <div className="mb-2 text-xs">
        <span className="font-semibold">CkTypeId:</span>
        <div className="ml-2 font-mono text-xs">
            {entity.CkTypeId.FullName || formatValue(entity.CkTypeId.Key)}
        </div>
    </div>
    )}
    {entity.RtWellKnownName && (
    <div className="mb-2">
        <span className="font-semibold">Name:</span> {entity.RtWellKnownName}
    </div>
    )}
    <div className="grid grid-cols-2 gap-2 text-xs">
        <div>
            <span className="text-gray-600">Version:</span> {entity.RtVersion}
        </div>
        <div>
            <span className="text-gray-600">State:</span> {entity.RtState}
        </div>
        {entity.RtCreationDateTime && (
        <div className="col-span-2">
            <span className="text-gray-600">Created:</span> {formatDate(entity.RtCreationDateTime)}
        </div>
        )}
        {entity.RtChangedDateTime && (
        <div className="col-span-2">
            <span className="text-gray-600">Modified:</span> {formatDate(entity.RtChangedDateTime)}
        </div>
        )}
    </div>
    {entity.Attributes && Object.keys(entity.Attributes).length > 0 && (
    <details className="mt-2">
        <summary className="cursor-pointer text-xs font-semibold text-gray-700">
            Attributes ({Object.keys(entity.Attributes).length})
        </summary>
        <pre className="mt-1 text-xs overflow-x-auto bg-white p-2 rounded">
              {formatValue(entity.Attributes)}
            </pre>
    </details>
    )}
</div>
);
};

const renderSummaryTab = () => (
<div className="space-y-6">
    <div className="bg-white rounded-lg shadow p-6">
        <h3 className="text-xl font-bold mb-4">Comparison Metadata</h3>
        <div className="grid grid-cols-2 gap-4 text-sm">
            <div>
                <span className="text-gray-600">Source Tenant:</span>
                <span className="ml-2 font-mono">{data.Metadata?.SourceTenantId}</span>
            </div>
            <div>
                <span className="text-gray-600">Target Tenant:</span>
                <span className="ml-2 font-mono">{data.Metadata?.TargetTenantId}</span>
            </div>
            <div>
                <span className="text-gray-600">Comparison Date:</span>
                <span className="ml-2">{formatDate(data.Metadata?.ComparisonDate)}</span>
            </div>
            <div>
                <span className="text-gray-600">Duration:</span>
                <span className="ml-2 font-mono">{data.Metadata?.Duration}</span>
            </div>
        </div>
    </div>

    <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
        <SummaryCard
                title="Total Differences"
                value={data.Summary?.TotalDifferences || 0}
                color={data.Summary?.AreIdentical ? "#10b981" : "#ef4444"}
        icon={data.Summary?.AreIdentical ? CheckCircle : AlertCircle}
        />
        <SummaryCard
                title="Status"
                value={data.Summary?.AreIdentical ? "Identical" : "Different"}
        color={data.Summary?.AreIdentical ? "#10b981" : "#f59e0b"}
        icon={Info}
        />
        <SummaryCard
                title="CkModel Diffs"
                value={data.Summary?.CkModelDifferences || 0}
                color="#8b5cf6"
                icon={AlertCircle}
        />
    </div>

    <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
        <SummaryCard
                title="CkType Differences"
                value={data.Summary?.CkTypeDifferences || 0}
                color="#3b82f6"
        />
        <SummaryCard
                title="RtEntity Differences"
                value={data.Summary?.RtEntityDifferences || 0}
                color="#ec4899"
        />
        <SummaryCard
                title="Association Diffs"
                value={data.Summary?.AssociationDifferences || 0}
                color="#14b8a6"
        />
    </div>
</div>
);

const renderMetadataTab = () => (
<div className="space-y-4">
    <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        <div className="bg-blue-50 border-2 border-blue-200 rounded-lg p-6">
            <h4 className="text-lg font-bold text-blue-900 mb-4">Source Environment</h4>
            <dl className="space-y-2 text-sm">
                <div className="flex justify-between">
                    <dt className="text-gray-600">Tenant ID:</dt>
                    <dd className="font-mono">{data.MetadataComparison?.Source?.TenantId}</dd>
                </div>
                <div className="flex justify-between">
                    <dt className="text-gray-600">Database:</dt>
                    <dd className="font-mono">{data.MetadataComparison?.Source?.DatabaseName}</dd>
                </div>
                <div className="flex justify-between">
                    <dt className="text-gray-600">Total Entities:</dt>
                    <dd className="font-bold">{data.MetadataComparison?.Source?.TotalRtEntityCount}</dd>
                </div>
                <div className="flex justify-between">
                    <dt className="text-gray-600">Associations:</dt>
                    <dd className="font-bold">{data.MetadataComparison?.Source?.TotalAssociationCount}</dd>
                </div>
                <div className="flex justify-between">
                    <dt className="text-gray-600">CkModels:</dt>
                    <dd className="font-bold">{data.MetadataComparison?.Source?.CkModelCount}</dd>
                </div>
            </dl>
        </div>

        <div className="bg-green-50 border-2 border-green-200 rounded-lg p-6">
            <h4 className="text-lg font-bold text-green-900 mb-4">Target Environment</h4>
            <dl className="space-y-2 text-sm">
                <div className="flex justify-between">
                    <dt className="text-gray-600">Tenant ID:</dt>
                    <dd className="font-mono">{data.MetadataComparison?.Target?.TenantId}</dd>
                </div>
                <div className="flex justify-between">
                    <dt className="text-gray-600">Database:</dt>
                    <dd className="font-mono">{data.MetadataComparison?.Target?.DatabaseName}</dd>
                </div>
                <div className="flex justify-between">
                    <dt className="text-gray-600">Total Entities:</dt>
                    <dd className="font-bold">{data.MetadataComparison?.Target?.TotalRtEntityCount}</dd>
                </div>
                <div className="flex justify-between">
                    <dt className="text-gray-600">Associations:</dt>
                    <dd className="font-bold">{data.MetadataComparison?.Target?.TotalAssociationCount}</dd>
                </div>
                <div className="flex justify-between">
                    <dt className="text-gray-600">CkModels:</dt>
                    <dd className="font-bold">{data.MetadataComparison?.Target?.CkModelCount}</dd>
                </div>
            </dl>
        </div>
    </div>

    {(data.MetadataComparison?.Source?.RtEntityCountByCkType || data.MetadataComparison?.Target?.RtEntityCountByCkType) && (
    <CollapsibleSection
            title="Entity Count by CkType"
            count={Object.keys(data.MetadataComparison?.Source?.RtEntityCountByCkType || data.MetadataComparison?.Target?.RtEntityCountByCkType || {}).length}
            colorClass="bg-indigo-100"
    >
        <div className="overflow-x-auto">
            <table className="w-full text-sm">
                <thead className="bg-gray-100 border-b-2 border-gray-300">
                <tr>
                    <th className="text-left p-2 font-semibold">CkType</th>
                    <th className="text-right p-2 font-semibold text-blue-700">Source Count</th>
                    <th className="text-right p-2 font-semibold text-green-700">Target Count</th>
                    <th className="text-right p-2 font-semibold text-gray-700">Difference</th>
                </tr>
                </thead>
                <tbody>
                {(() => {
                const sourceCounts = data.MetadataComparison?.Source?.RtEntityCountByCkType || {};
                const targetCounts = data.MetadataComparison?.Target?.RtEntityCountByCkType || {};
                const allTypes = new Set([...Object.keys(sourceCounts), ...Object.keys(targetCounts)]);

                return Array.from(allTypes).sort().map((ckType, idx) => {
                const sourceCount = sourceCounts[ckType] || 0;
                const targetCount = targetCounts[ckType] || 0;
                const diff = targetCount - sourceCount;
                const hasDifference = diff !== 0;

                return (
                <tr key={idx} className={`border-b ${hasDifference ? 'bg-yellow-50' : ''}`}>
                <td className="p-2 font-mono text-xs">{ckType}</td>
                <td className="p-2 text-right text-blue-700 font-semibold">{sourceCount}</td>
                <td className="p-2 text-right text-green-700 font-semibold">{targetCount}</td>
                <td className={`p-2 text-right font-semibold ${
                    diff > 0 ? 'text-green-600' : diff < 0 ? 'text-red-600' : 'text-gray-500'
                    }`}>
                    {diff > 0 ? '+' : ''}{diff}
                </td>
                </tr>
                );
                });
                })()}
                </tbody>
            </table>
        </div>
    </CollapsibleSection>
    )}

    {data.MetadataComparison?.Differences?.length > 0 && (
    <CollapsibleSection
            title="Metadata Differences"
            count={data.MetadataComparison.Differences.length}
            colorClass="bg-yellow-100"
    >
        <div className="space-y-2">
            {data.MetadataComparison.Differences.map((diff, idx) => (
            <div key={idx} className="p-3 bg-yellow-50 rounded border border-yellow-200">
                <p className="font-semibold text-sm">{diff.FieldName}</p>
                <p className="text-sm text-gray-600 mt-1">{diff.Description}</p>
                <div className="flex gap-4 mt-2 text-sm">
                    <span className="text-blue-600">Source: {JSON.stringify(diff.SourceValue)}</span>
                    <span className="text-green-600">Target: {JSON.stringify(diff.TargetValue)}</span>
                </div>
            </div>
            ))}
        </div>
    </CollapsibleSection>
    )}
</div>
);

const renderCkModelTab = () => {
const comparison = data.CkModelComparison;
if (!comparison) {
return <div className="text-gray-600">No CkModel comparison data available</div>;
}

return (
<div className="space-y-4">
    <div className="grid grid-cols-1 md:grid-cols-4 gap-4 mb-4">
        <div className="bg-red-50 border border-red-200 rounded p-4">
            <div className="text-2xl font-bold text-red-600">{comparison.OnlyInSource?.length || 0}</div>
            <div className="text-sm text-gray-600">Only in Source</div>
        </div>
        <div className="bg-green-50 border border-green-200 rounded p-4">
            <div className="text-2xl font-bold text-green-600">{comparison.OnlyInTarget?.length || 0}</div>
            <div className="text-sm text-gray-600">Only in Target</div>
        </div>
        <div className="bg-blue-50 border border-blue-200 rounded p-4">
            <div className="text-2xl font-bold text-blue-600">{comparison.InBothSameVersion?.length || 0}</div>
            <div className="text-sm text-gray-600">Same Version</div>
        </div>
        <div className="bg-yellow-50 border border-yellow-200 rounded p-4">
            <div className="text-2xl font-bold text-yellow-600">{comparison.VersionDifferences?.length || 0}</div>
            <div className="text-sm text-gray-600">Version Diffs</div>
        </div>
    </div>

    {comparison.OnlyInSource?.length > 0 && (
    <CollapsibleSection title="Only in Source" count={comparison.OnlyInSource.length} colorClass="bg-red-100">
        <div className="space-y-2">
            {comparison.OnlyInSource.map((model, idx) => (
            <div key={idx} className="p-3 bg-red-50 border border-red-200 rounded">
                <div className="font-mono text-sm">{formatValue(model)}</div>
            </div>
            ))}
        </div>
    </CollapsibleSection>
    )}

    {comparison.OnlyInTarget?.length > 0 && (
    <CollapsibleSection title="Only in Target" count={comparison.OnlyInTarget.length} colorClass="bg-green-100">
        <div className="space-y-2">
            {comparison.OnlyInTarget.map((model, idx) => (
            <div key={idx} className="p-3 bg-green-50 border border-green-200 rounded">
                <div className="font-mono text-sm">{formatValue(model)}</div>
            </div>
            ))}
        </div>
    </CollapsibleSection>
    )}

    {comparison.VersionDifferences?.length > 0 && (
    <CollapsibleSection title="Version Differences" count={comparison.VersionDifferences.length} colorClass="bg-yellow-100">
        <div className="space-y-3">
            {comparison.VersionDifferences.map((diff, idx) => (
            <div key={idx} className="p-3 bg-yellow-50 border border-yellow-200 rounded">
                <div className="font-mono text-sm font-semibold mb-2">{diff.ModelKey}</div>
                <div className="grid grid-cols-2 gap-3">
                    <div className="bg-blue-50 p-2 rounded">
                        <div className="text-xs font-semibold text-blue-900">Source Version</div>
                        <pre className="text-xs mt-1 overflow-x-auto">{formatValue(diff.SourceVersion)}</pre>
                    </div>
                    <div className="bg-green-50 p-2 rounded">
                        <div className="text-xs font-semibold text-green-900">Target Version</div>
                        <pre className="text-xs mt-1 overflow-x-auto">{formatValue(diff.TargetVersion)}</pre>
                    </div>
                </div>
            </div>
            ))}
        </div>
    </CollapsibleSection>
    )}
</div>
);
};

const renderCkTypeTab = () => {
const comparison = data.CkTypeComparison;
if (!comparison) {
return <div className="text-gray-600">No CkType comparison data available</div>;
}

const renderTypeCard = (type, bgColor, borderColor) => (
<div className={`p-3 ${bgColor} border ${borderColor} rounded`}>
    <div className="font-mono text-sm font-semibold">{type.CkTypeId}</div>
    {type.Description && (
    <div className="text-xs text-gray-600 mt-1">{type.Description}</div>
    )}
    <div className="grid grid-cols-2 gap-2 text-xs mt-2">
        <div>
            <span className="text-gray-600">Collection Root:</span> {type.IsCollectionRoot ? '✓' : '✗'}
        </div>
        <div>
            <span className="text-gray-600">Stream Type:</span> {type.IsStreamType ? '✓' : '✗'}
        </div>
        <div>
            <span className="text-gray-600">Final:</span> {type.IsFinal ? '✓' : '✗'}
        </div>
        <div>
            <span className="text-gray-600">Abstract:</span> {type.IsAbstract ? '✓' : '✗'}
        </div>
    </div>
    {type.DerivedFromCkTypeId && (
    <div className="text-xs mt-2">
        <span className="text-gray-600">Derived from:</span> {type.DerivedFromCkTypeId}
    </div>
    )}
    <div className="grid grid-cols-3 gap-2 text-xs mt-2">
        <div>
            <span className="text-gray-600">Attributes:</span> {type.AttributeIds?.length || 0}
        </div>
        <div>
            <span className="text-gray-600">Incoming:</span> {type.IncomingAssociationsCount || 0}
        </div>
        <div>
            <span className="text-gray-600">Outgoing:</span> {type.OutgoingAssociationsCount || 0}
        </div>
        <div className="col-span-3">
            <span className="text-gray-600">Indexes:</span> {type.IndexesCount || 0}
        </div>
    </div>
    {type.AttributeIds && type.AttributeIds.length > 0 && (
    <details className="mt-2">
        <summary className="cursor-pointer text-xs font-semibold text-gray-700">
            Attribute IDs ({type.AttributeIds.length})
        </summary>
        <div className="mt-1 text-xs bg-white p-2 rounded max-h-40 overflow-y-auto">
            {type.AttributeIds.map((attrId, i) => (
            <div key={i} className="font-mono">{attrId}</div>
            ))}
        </div>
    </details>
    )}
</div>
);

return (
<div className="space-y-4">
    <div className="grid grid-cols-1 md:grid-cols-4 gap-4 mb-4">
        <div className="bg-red-50 border border-red-200 rounded p-4">
            <div className="text-2xl font-bold text-red-600">{comparison.OnlyInSource?.length || 0}</div>
            <div className="text-sm text-gray-600">Only in Source</div>
        </div>
        <div className="bg-green-50 border border-green-200 rounded p-4">
            <div className="text-2xl font-bold text-green-600">{comparison.OnlyInTarget?.length || 0}</div>
            <div className="text-sm text-gray-600">Only in Target</div>
        </div>
        <div className="bg-blue-50 border border-blue-200 rounded p-4">
            <div className="text-2xl font-bold text-blue-600">{comparison.InBothSame?.length || 0}</div>
            <div className="text-sm text-gray-600">Identical</div>
        </div>
        <div className="bg-yellow-50 border border-yellow-200 rounded p-4">
            <div className="text-2xl font-bold text-yellow-600">{comparison.Differences?.length || 0}</div>
            <div className="text-sm text-gray-600">Differences</div>
        </div>
    </div>

    {comparison.OnlyInSource?.length > 0 && (
    <CollapsibleSection title="Only in Source" count={comparison.OnlyInSource.length} colorClass="bg-red-100">
        <div className="space-y-2 max-h-96 overflow-y-auto">
            {comparison.OnlyInSource.slice(0, 50).map((type, idx) => (
            <div key={idx}>
                {renderTypeCard(type, 'bg-red-50', 'border-red-200')}
            </div>
            ))}
            {comparison.OnlyInSource.length > 50 && (
            <div className="text-sm text-gray-600 text-center p-2">
                ... and {comparison.OnlyInSource.length - 50} more
            </div>
            )}
        </div>
    </CollapsibleSection>
    )}

    {comparison.OnlyInTarget?.length > 0 && (
    <CollapsibleSection title="Only in Target" count={comparison.OnlyInTarget.length} colorClass="bg-green-100">
        <div className="space-y-2 max-h-96 overflow-y-auto">
            {comparison.OnlyInTarget.slice(0, 50).map((type, idx) => (
            <div key={idx}>
                {renderTypeCard(type, 'bg-green-50', 'border-green-200')}
            </div>
            ))}
            {comparison.OnlyInTarget.length > 50 && (
            <div className="text-sm text-gray-600 text-center p-2">
                ... and {comparison.OnlyInTarget.length - 50} more
            </div>
            )}
        </div>
    </CollapsibleSection>
    )}

    {comparison.Differences?.length > 0 && (
    <CollapsibleSection title="Type Differences" count={comparison.Differences.length} colorClass="bg-yellow-100">
        <div className="space-y-3 max-h-96 overflow-y-auto">
            {comparison.Differences.slice(0, 50).map((diff, idx) => (
            <div key={idx} className="p-3 bg-yellow-50 border border-yellow-200 rounded">
                <div className="font-mono text-sm font-semibold mb-2">{diff.CkTypeId}</div>
                <div className="text-sm text-gray-700 mb-3">{diff.Description}</div>
                <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                    <div>
                        <h6 className="text-xs font-semibold text-blue-900 mb-1">Source Type</h6>
                        {renderTypeCard(diff.SourceType, 'bg-blue-50', 'border-blue-200')}
                    </div>
                    <div>
                        <h6 className="text-xs font-semibold text-green-900 mb-1">Target Type</h6>
                        {renderTypeCard(diff.TargetType, 'bg-green-50', 'border-green-200')}
                    </div>
                </div>
            </div>
            ))}
            {comparison.Differences.length > 50 && (
            <div className="text-sm text-gray-600 text-center p-2">
                ... and {comparison.Differences.length - 50} more
            </div>
            )}
        </div>
    </CollapsibleSection>
    )}
</div>
);
};

const renderRtEntityTab = () => {
const comparisons = data.RtEntityComparisons;
if (!comparisons || Object.keys(comparisons).length === 0) {
return <div className="text-gray-600">No RtEntity comparison data available</div>;
}

return (
<div className="space-y-4">
    {Object.entries(comparisons).map(([key, comparison]) => {
    const totalDiffs = (comparison.OnlyInSource?.length || 0) +
    (comparison.OnlyInTarget?.length || 0) +
    (comparison.Differences?.length || 0);

    if (totalDiffs === 0) return null;

    return (
    <CollapsibleSection
            key={key}
            title={`${comparison.CkTypeId?.FullName || key}`}
            count={totalDiffs}
            colorClass="bg-purple-100"
    >
        <div className="space-y-4">
            <div className="grid grid-cols-2 md:grid-cols-5 gap-3 mb-4">
                <div className="bg-blue-50 border border-blue-200 rounded p-3">
                    <div className="text-lg font-bold text-blue-600">{comparison.SourceTotalCount}</div>
                    <div className="text-xs text-gray-600">Source Total</div>
                </div>
                <div className="bg-green-50 border border-green-200 rounded p-3">
                    <div className="text-lg font-bold text-green-600">{comparison.TargetTotalCount}</div>
                    <div className="text-xs text-gray-600">Target Total</div>
                </div>
                <div className="bg-red-50 border border-red-200 rounded p-3">
                    <div className="text-lg font-bold text-red-600">{comparison.OnlyInSource?.length || 0}</div>
                    <div className="text-xs text-gray-600">Only Source</div>
                </div>
                <div className="bg-green-50 border border-green-200 rounded p-3">
                    <div className="text-lg font-bold text-green-600">{comparison.OnlyInTarget?.length || 0}</div>
                    <div className="text-xs text-gray-600">Only Target</div>
                </div>
                <div className="bg-yellow-50 border border-yellow-200 rounded p-3">
                    <div className="text-lg font-bold text-yellow-600">{comparison.Differences?.length || 0}</div>
                    <div className="text-xs text-gray-600">Modified</div>
                </div>
            </div>

            {comparison.OnlyInSource?.length > 0 && (
            <div>
                <h5 className="font-semibold text-red-700 mb-2">Only in Source ({comparison.OnlyInSource.length})</h5>
                <div className="space-y-2 max-h-96 overflow-y-auto">
                    {comparison.OnlyInSource.slice(0, 20).map((entity, idx) => (
                    <EntityCard key={idx} entity={entity} type="source" />
                    ))}
                    {comparison.OnlyInSource.length > 20 && (
                    <div className="text-sm text-gray-600 text-center p-2 bg-gray-50 rounded">
                        ... and {comparison.OnlyInSource.length - 20} more entities
                    </div>
                    )}
                </div>
            </div>
            )}

            {comparison.OnlyInTarget?.length > 0 && (
            <div>
                <h5 className="font-semibold text-green-700 mb-2">Only in Target ({comparison.OnlyInTarget.length})</h5>
                <div className="space-y-2 max-h-96 overflow-y-auto">
                    {comparison.OnlyInTarget.slice(0, 20).map((entity, idx) => (
                    <EntityCard key={idx} entity={entity} type="target" />
                    ))}
                    {comparison.OnlyInTarget.length > 20 && (
                    <div className="text-sm text-gray-600 text-center p-2 bg-gray-50 rounded">
                        ... and {comparison.OnlyInTarget.length - 20} more entities
                    </div>
                    )}
                </div>
            </div>
            )}

            {comparison.Differences?.length > 0 && (
            <div>
                <h5 className="font-semibold text-yellow-700 mb-2">Modified Entities ({comparison.Differences.length})</h5>
                <div className="space-y-3 max-h-96 overflow-y-auto">
                    {comparison.Differences.slice(0, 20).map((diff, idx) => {
                    const entityKey = `diff-${key}-${idx}`;
                    const isExpanded = expandedEntities[entityKey];

                    return (
                    <div key={idx} className="border border-yellow-300 rounded-lg overflow-hidden">
                        <button
                                onClick={() => toggleEntity(entityKey)}
                        className="w-full bg-yellow-100 px-3 py-2 flex items-center justify-between hover:bg-yellow-200"
                        >
                        <div className="flex items-center gap-2">
                            {isExpanded ? <ChevronDown size={16} /> : <ChevronRight size={16} />}
                            <span className="font-mono text-sm">
                                  {formatValue(diff.SourceEntity?.RtId) || formatValue(diff.TargetEntity?.RtId)}
                                </span>
                            <span className="text-xs bg-yellow-200 px-2 py-1 rounded">
                                  {diff.DifferenceCount} changes
                                </span>
                        </div>
                        <span className="text-xs text-gray-600">
                                Matched by: {diff.MatchedBy}
                              </span>
                        </button>
                        {isExpanded && (
                        <div className="p-3 bg-white">
                            <div className="grid grid-cols-1 md:grid-cols-2 gap-3 mb-3">
                                <div>
                                    <h6 className="text-xs font-semibold text-blue-900 mb-2">Source Entity</h6>
                                    <EntityCard entity={diff.SourceEntity} type="source" />
                                </div>
                                <div>
                                    <h6 className="text-xs font-semibold text-green-900 mb-2">Target Entity</h6>
                                    <EntityCard entity={diff.TargetEntity} type="target" />
                                </div>
                            </div>
                            {diff.PropertyDifferences?.length > 0 && (
                            <div>
                                <h6 className="text-sm font-semibold mb-2">Property Differences</h6>
                                <div className="space-y-1">
                                    {diff.PropertyDifferences.map((propDiff, pIdx) => (
                                    <div key={pIdx} className="bg-gray-50 p-2 rounded text-xs">
                                        <div className="font-semibold">{propDiff.PropertyName}</div>
                                        <div className="grid grid-cols-2 gap-2 mt-1">
                                            <div className="text-blue-600">
                                                Source: {formatValue(propDiff.SourceValue)}
                                            </div>
                                            <div className="text-green-600">
                                                Target: {formatValue(propDiff.TargetValue)}
                                            </div>
                                        </div>
                                    </div>
                                    ))}
                                </div>
                            </div>
                            )}
                        </div>
                        )}
                    </div>
                    );
                    })}
                    {comparison.Differences.length > 20 && (
                    <div className="text-sm text-gray-600 text-center p-2 bg-gray-50 rounded">
                        ... and {comparison.Differences.length - 20} more modified entities
                    </div>
                    )}
                </div>
            </div>
            )}
        </div>
    </CollapsibleSection>
    );
    })}
</div>
);
};

const renderAssociationTab = () => {
const comparison = data.AssociationComparison;
if (!comparison) {
return <div className="text-gray-600">No Association comparison data available</div>;
}

return (
<div className="space-y-4">
    <div className="grid grid-cols-1 md:grid-cols-4 gap-4 mb-4">
        <div className="bg-blue-50 border border-blue-200 rounded p-4">
            <div className="text-2xl font-bold text-blue-600">{comparison.SourceTotalCount || 0}</div>
            <div className="text-sm text-gray-600">Source Total</div>
        </div>
        <div className="bg-green-50 border border-green-200 rounded p-4">
            <div className="text-2xl font-bold text-green-600">{comparison.TargetTotalCount || 0}</div>
            <div className="text-sm text-gray-600">Target Total</div>
        </div>
        <div className="bg-yellow-50 border border-yellow-200 rounded p-4">
            <div className="text-2xl font-bold text-yellow-600">{comparison.MatchedAssociationCount || 0}</div>
            <div className="text-sm text-gray-600">Matched</div>
        </div>
        <div className="bg-red-50 border border-red-200 rounded p-4">
            <div className="text-2xl font-bold text-red-600">{comparison.TotalDifferences || 0}</div>
            <div className="text-sm text-gray-600">Differences</div>
        </div>
    </div>

    {comparison.OnlyInSource?.length > 0 && (
    <CollapsibleSection title="Only in Source" count={comparison.OnlyInSource.length} colorClass="bg-red-100">
        <div className="space-y-2 max-h-96 overflow-y-auto">
            {comparison.OnlyInSource.slice(0, 50).map((assoc, idx) => (
            <div key={idx} className="p-3 bg-red-50 border border-red-200 rounded text-sm">
                <div className="font-mono text-xs mb-2">
                    ID: {formatValue(assoc.AssociationId)}
                </div>
                <div className="grid grid-cols-2 gap-2 text-xs">
                    <div>
                        <span className="text-gray-600">Origin:</span> {formatValue(assoc.OriginRtId)}
                    </div>
                    <div>
                        <span className="text-gray-600">Target:</span> {formatValue(assoc.TargetRtId)}
                    </div>
                    <div className="col-span-2">
                        <span className="text-gray-600">Origin Type:</span> {assoc.OriginCkTypeId?.FullName}
                    </div>
                    <div className="col-span-2">
                        <span className="text-gray-600">Target Type:</span> {assoc.TargetCkTypeId?.FullName}
                    </div>
                </div>
            </div>
            ))}
            {comparison.OnlyInSource.length > 50 && (
            <div className="text-sm text-gray-600 text-center p-2">
                ... and {comparison.OnlyInSource.length - 50} more
            </div>
            )}
        </div>
    </CollapsibleSection>
    )}

    {comparison.OnlyInTarget?.length > 0 && (
    <CollapsibleSection title="Only in Target" count={comparison.OnlyInTarget.length} colorClass="bg-green-100">
        <div className="space-y-2 max-h-96 overflow-y-auto">
            {comparison.OnlyInTarget.slice(0, 50).map((assoc, idx) => (
            <div key={idx} className="p-3 bg-green-50 border border-green-200 rounded text-sm">
                <div className="font-mono text-xs mb-2">
                    ID: {formatValue(assoc.AssociationId)}
                </div>
                <div className="grid grid-cols-2 gap-2 text-xs">
                    <div>
                        <span className="text-gray-600">Origin:</span> {formatValue(assoc.OriginRtId)}
                    </div>
                    <div>
                        <span className="text-gray-600">Target:</span> {formatValue(assoc.TargetRtId)}
                    </div>
                    <div className="col-span-2">
                        <span className="text-gray-600">Origin Type:</span> {assoc.OriginCkTypeId?.FullName}
                    </div>
                    <div className="col-span-2">
                        <span className="text-gray-600">Target Type:</span> {assoc.TargetCkTypeId?.FullName}
                    </div>
                </div>
            </div>
            ))}
            {comparison.OnlyInTarget.length > 50 && (
            <div className="text-sm text-gray-600 text-center p-2">
                ... and {comparison.OnlyInTarget.length - 50} more
            </div>
            )}
        </div>
    </CollapsibleSection>
    )}
</div>
);
};

const handleFileUpload = (event) => {
const file = event.target.files[0];
if (file) {
const reader = new FileReader();
reader.onload = (e) => {
try {
const json = JSON.parse(e.target.result);
setData(json);
setExpandedSections({});
setExpandedEntities({});
} catch (error) {
alert('Error parsing JSON file: ' + error.message);
}
};
reader.readAsText(file);
}
};

return (
<div className="min-h-screen bg-gray-100 p-6">
    <div className="max-w-7xl mx-auto">
        <div className="bg-white rounded-lg shadow-lg p-6 mb-6">
            <div className="flex items-center justify-between">
                <div>
                    <h1 className="text-3xl font-bold text-gray-900">Octomesh Tenant Comparison</h1>
                    <p className="text-gray-600 mt-1">Compare runtime data, CkTypes, and metadata across tenants</p>
                </div>
                <label className="px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 cursor-pointer transition-colors">
                    Load JSON
                    <input
                            type="file"
                            accept=".json"
                            onChange={handleFileUpload}
                            className="hidden"
                    />
                </label>
            </div>
        </div>

        <div className="bg-white rounded-lg shadow-lg overflow-hidden">
            <div className="border-b border-gray-200">
                <nav className="flex overflow-x-auto">
                    {[
                    { id: 'summary', label: 'Summary', badge: data.Summary?.TotalDifferences },
                    { id: 'metadata', label: 'Metadata', badge: data.Summary?.MetadataDifferences },
                    { id: 'ckmodel', label: 'CkModels', badge: data.Summary?.CkModelDifferences },
                    { id: 'cktype', label: 'CkTypes', badge: data.Summary?.CkTypeDifferences },
                    { id: 'rtentity', label: 'RtEntities', badge: data.Summary?.RtEntityDifferences },
                    { id: 'association', label: 'Associations', badge: data.Summary?.AssociationDifferences }
                    ].map(tab => (
                    <button
                            key={tab.id}
                            onClick={() => setActiveTab(tab.id)}
                    className={`px-6 py-4 text-sm font-medium whitespace-nowrap border-b-2 transition-colors ${
                    activeTab === tab.id
                    ? 'border-blue-600 text-blue-600'
                    : 'border-transparent text-gray-600 hover:text-gray-900 hover:border-gray-300'
                    }`}
                    >
                    {tab.label}
                    {tab.badge > 0 && (
                    <span className="ml-2 px-2 py-1 bg-red-100 text-red-600 text-xs rounded-full">
                      {tab.badge}
                    </span>
                    )}
                    </button>
                    ))}
                </nav>
            </div>

            <div className="p-6">
                {activeTab === 'summary' && renderSummaryTab()}
                {activeTab === 'metadata' && renderMetadataTab()}
                {activeTab === 'ckmodel' && renderCkModelTab()}
                {activeTab === 'cktype' && renderCkTypeTab()}
                {activeTab === 'rtentity' && renderRtEntityTab()}
                {activeTab === 'association' && renderAssociationTab()}
            </div>
        </div>
    </div>
</div>
);
};

export default ComparisonViewer;

// Make the component available globally when using UMD build
if (typeof window !== 'undefined') {
    (window as any).ComparisonViewerComponent = ComparisonViewer;
}