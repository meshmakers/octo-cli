namespace Meshmakers.Octo.Frontend.ManagementTool;

public class GraphQl
{
    internal const string CreateLargeBinary = @"mutation ($binaryData: LargeBinary!) {
          sysCreateLargeBinary(binaryData: $binaryData)
        }";

    internal const string DownloadLargeBinary = @"query ($id: OctoObjectIdType!) {
          sysDownloadLargeBinary(largeBinaryId: $id)
        }";

    internal const string CreateServiceHook = @"mutation createServiceHook($entities: [SystemServiceHookInput]!) {
          createSystemServiceHooks(entities: $entities) {
            rtId
            enabled
            name
            queryCkId
            serviceHookAction
            serviceHookBaseUri
            fieldFilter
          }
        }";

    internal const string UpdateServiceHook =
        @"mutation updateServiceHook($entities: [UpdateSystemServiceHookInput]!) {
              updateSystemServiceHooks(entities: $entities) {
                rtId
                enabled
                name
                queryCkId
                serviceHookAction
                serviceHookBaseUri
                fieldFilter
              }
            }
            ";

    internal const string DeleteServiceHook =
        @"mutation deleteServiceHook($entities: [DeletionSystemServiceHookInput]!) {
            deleteSystemServiceHooks(entities: $entities)
          }";

    internal const string GetServiceHook = @"query getServiceHooks(
          $after: String,
          $first: Int,
          $rtIds:[OctoObjectIdType],
          $searchFilter: SearchFilter,
          $fieldFilters: [FieldFilter],
          $sort: [Sort]) {
          systemServiceHookConnection(
            after: $after
            first: $first
            rtIds: $rtIds
            searchFilter: $searchFilter
            fieldFilter: $fieldFilters
            sortOrder: $sort
          ) {
            totalCount

            items {
              rtId
              enabled
              name
              queryCkId
              serviceHookAction
              serviceHookBaseUri
              serviceHookApiKey
            }
          }
        }
        ";

    internal const string GetServiceHookDetails = @"query getServiceHookDetails($rtId: String!) {
          systemServiceHookConnection(rtId: $rtId) {
            totalCount

            items {
              rtId
              enabled
              name
              queryCkId
              serviceHookAction
              serviceHookBaseUri
              serviceHookApiKey
              fieldFilter
            }
          }
        }";

    internal const string GetNotifications = @"query getNotifications(
          $after: String
          $first: Int
          $searchFilter: SearchFilter
          $fieldFilters: [FieldFilter]
          $sort: [Sort]
        ) {
          systemNotificationMessageConnection(
            after: $after
            first: $first
            searchFilter: $searchFilter
            fieldFilter: $fieldFilters
            sortOrder: $sort
          ) {
            totalCount
            items {
              rtId
            }
          }
        }";
}
