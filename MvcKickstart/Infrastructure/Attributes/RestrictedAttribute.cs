﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Principal;
using System.Threading;
using System.Web;
using System.Web.Mvc;
using System.Web.Security;
using MvcKickstart.Infrastructure.Extensions;
using MvcKickstart.Models.Users;
using MvcKickstart.ViewModels.Shared;
using Raven.Client;
using ServiceStack.CacheAccess;

namespace MvcKickstart.Infrastructure.Attributes
{
	public class RestrictedAttribute : AuthorizeAttribute
	{
		public bool RequireAdmin { get; set; }
		protected ICacheClient Cache { get; private set; }
		protected IDocumentSession RavenSession { get; private set; }

	/// <summary>
		/// The key to the authentication token that should be submitted somewhere in the request.
		/// </summary>
		private const string TokenKey = "AuthenticationToken";

		public RestrictedAttribute()
		{
			Cache = StructureMap.ObjectFactory.GetInstance<ICacheClient>();
			RavenSession = StructureMap.ObjectFactory.GetInstance<IDocumentSession>();
		}
		protected override bool AuthorizeCore(HttpContextBase httpContext)
		{
			//If not authenticated, it might be a request from flash in Firefox, so get the auth token passed in to create Identity
			if (!httpContext.Request.IsAuthenticated)
			{
				var token = httpContext.Request.Params[TokenKey];
				if (token != null)
				{
					var ticket = FormsAuthentication.Decrypt(token);
					if (ticket != null)
					{
						var identity = new FormsIdentity(ticket);
						httpContext.User = new GenericPrincipal(identity, null);	//this doesn't need to be a UserPrincipal, because that will happen below
					}
				}
			}

			if (!httpContext.Request.IsAuthenticated)
				return false;

			// If it's not a UserPrincipal, we need to create it (b/c this happens before RavenController.OnAuthorization)
			if (!(httpContext.User is UserPrincipal))
			{
				User userObject;
				if (httpContext.User.Identity.IsAuthenticated && httpContext.User.Identity.AuthenticationType == "Forms")
				{
					using (RavenSession.GetCachingContext())
					{
						userObject = RavenSession.Query<User>().Customize(x => x.WaitForNonStaleResults()).SingleOrDefault(x => x.Username == httpContext.User.Identity.Name);
					}
				}
				else
				{
					userObject = new User();
				}

				httpContext.User = new UserPrincipal(userObject, httpContext.User.Identity);
				Thread.CurrentPrincipal = httpContext.User;
			}

			var user = httpContext.User as UserPrincipal;

			return !RequireAdmin || user.IsAdmin;
		}
		protected override void HandleUnauthorizedRequest(AuthorizationContext filterContext)
		{
			if (filterContext.HttpContext.User.Identity.IsAuthenticated)
			{
				filterContext.Result = filterContext.HttpContext.Request.Url != null
											? new RedirectResult("~/No-Permission?returnUrl=" + filterContext.HttpContext.Request.Url.AbsolutePath)
											: new RedirectResult("~/No-Permission");
			}
			else
			{
				if (filterContext.HttpContext.Request.IsAjaxRequest())
				{
					var urlHelper = new UrlHelper(filterContext.RequestContext);
					var returnUrl = filterContext.HttpContext.Request.Url != null
										? filterContext.HttpContext.Request.Url.AbsolutePath
										: string.Empty;
					filterContext.Result = new JsonNetResult
					{
						Data = new RedirectError
						{
							Message = "You must be logged in to perform this action.",
							ErrorCode = (int) HttpStatusCode.Unauthorized,
							RedirectUrl = urlHelper.Account().Login(returnUrl)
						}
					};
				}
				else
					base.HandleUnauthorizedRequest(filterContext);
			}
		}
	}
}