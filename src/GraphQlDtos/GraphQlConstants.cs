namespace GraphQlDtos;

public static class GraphQlConstants
{
    /// <summary>
    ///     The runtime id of an API scope by name: the CK methods of <c>System.Identity/ApiScope</c> address the scope by
    ///     runtime id, the identity REST DTO carries none.
    /// </summary>
    public const string GetApiScopeRtIdByName = @"
      query getApiScopeRtIdByName($name: SimpleScalar!) {
        runtime {
          systemIdentityApiScope(
            fieldFilter: [
              { attributePath: ""name"", operator: EQUALS, comparisonValue: $name }
            ]
          ) {
            totalCount
            items {
              rtId
            }
          }
        }
      }";

    /// <summary>
    ///     The runtime id of an API resource by name: the CK methods of <c>System.Identity/ApiResource</c> address the
    ///     resource by runtime id, the identity REST DTO carries none.
    /// </summary>
    public const string GetApiResourceRtIdByName = @"
      query getApiResourceRtIdByName($name: SimpleScalar!) {
        runtime {
          systemIdentityApiResource(
            fieldFilter: [
              { attributePath: ""name"", operator: EQUALS, comparisonValue: $name }
            ]
          ) {
            totalCount
            items {
              rtId
            }
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