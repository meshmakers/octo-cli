namespace GraphQlDtos;

public static class GraphQlConstants
{
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

    public const string GetFixupScriptByName = @"
      query getFixupScriptByName($name: SimpleScalar!) {
        runtime {
          systemBotFixup(
            fieldFilter: [
              { attributePath: ""name"", operator: EQUALS, comparisonValue: $name }
            ]
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