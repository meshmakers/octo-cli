namespace GraphQlDtos;

public static class GraphQlConstants
{
    public const string CreateServiceHook = @"mutation createServiceHook($entities: [SystemServiceHookInput]!) {
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

    public const string UpdateServiceHook =
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

    public const string DeleteServiceHook =
        @"mutation deleteServiceHook($entities: [DeletionSystemServiceHookInput]!) {
            deleteSystemServiceHooks(entities: $entities)
          }";

    public const string GetServiceHook = @"query getServiceHooks(
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

    public const string GetServiceHookDetails = @"query getServiceHookDetails($rtId: String!) {
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

    public const string GetNotifications = @"query getNotifications(
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

    public const string GetFixupScriptsQuery = @"
      query getFixups (
        $after: String
        $first: Int
        $rtIds: [OctoObjectIdType]
        $searchFilter: SearchFilter
        $fieldFilters: [FieldFilter]
        $sort: [Sort]
      ) {
        runtime {
          systemBotFixup(
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
              ckTypeId
              rtVersion
              name
              enabled
              isApplied
              appliedAt
              isSuccess
            }
          }
        }
      }";

    public const string GetFixupScriptsDetailsQuery = @"
      query getFixups (
        $rtId: OctoObjectIdType!
      ) {
        runtime {
          systemBotFixup(
            rtId: $rtId
          ) {
            totalCount
            items {
              rtId
              ckTypeId
              rtVersion
              name
              enabled
              script
              isApplied
              appliedAt
              isSuccess
              error
              output
            }
          }
        }
      }";

    public const string CreateFixupScript = @"
      mutation createEntity($entity: SystemBotFixupInput!) {
        runtime {
          systemBotFixups {
            create(entities: [$entity]) {
              rtId
              ckTypeId
              rtVersion
              name
              enabled
              script
              isApplied
              appliedAt
              isSuccess
              error
              output
            }
          }
        }
      }";

    public const string UpdateFixupScript = @"
      mutation updateFixupScript($entity: SystemBotFixupInputUpdate!) {
        runtime {
          systemBotFixups {
            update(entities: [$entity]) {
              rtId
              ckTypeId
              rtVersion
              name
              enabled
              script
              isApplied
              appliedAt
              isSuccess
              error
              output
            }
          }
        }
      }";
}