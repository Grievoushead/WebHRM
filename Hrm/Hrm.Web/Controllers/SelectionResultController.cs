﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using AutoMapper;
using Hrm.Data.EF.Models;
using Hrm.Data.EF.Repositories.Contracts;
using Hrm.Data.EF.Specifications.Implementations.Common;
using Hrm.Web.Controllers.Base;
using Hrm.Web.Models.Selection;
using Hrm.Web.Models.SelectionResult;
using KendoWrapper.Grid;
using KendoWrapper.Grid.Context;

namespace Hrm.Web.Controllers
{
    public class SelectionResultController : BaseController
    {
        private readonly IRepository<JobApplication> jobAppRepo;

        private readonly IRepository<Job> jobsRepo; 

        public SelectionResultController(IRepository<User> usersRepo, IRepository<JobApplication> jobAppRepo, IRepository<Job> jobsRepo) 
            : base(usersRepo)
        {
            this.jobAppRepo = jobAppRepo;
            this.jobsRepo = jobsRepo;
        }

        private long CurrentProjectId
        {
            get { return long.Parse(Session["ProjectId"].ToString()); }
            set { Session["ProjectId"] = value; }
        }

        // Selection result for Project Id
        public ActionResult Index(long id)
        {
            this.CurrentProjectId = id;
            return View();
        }

        public JsonResult GetGridData(GridContext ctx)
        {
            IQueryable<User> query = this.usersRepo.Where(x => x.JobApplications.Any(a=>a.Job.ProjectId == this.CurrentProjectId));
            var totalCount = query.Count();

            if (ctx.HasFilters)
            {
                query = ctx.ApplyFilters(query);
                totalCount = query.Count();
            }

            if (ctx.HasSorting)
            {
                switch (ctx.SortOrder)
                {
                    case SortOrder.Asc:
                        query = this.usersRepo.SortByAsc(ctx.SortColumn, query);
                        break;

                    case SortOrder.Desc:
                        query = this.usersRepo.SortByDesc(ctx.SortColumn, query);
                        break;
                }
            }

            var users = query.OrderBy(x => x.Id).Skip(ctx.Skip).Take(ctx.Take).ToList();

            var data = new List<CandidateModel>();
            foreach (var user in users)
            {
                var candidate = Mapper.Map<CandidateModel>(user);
                // HasSelected
                if (user.Jobs.Any(x => x.ProjectId == this.CurrentProjectId))
                {
                    candidate.HasSelected = true;
                }
                else // Not Selected
                {
                    candidate.HasSelected = false;
                }
                // Has Tested
                if (user.TestResults.Any(x=>user.AssignedTests.Any(a=>a.Id == x.TestId)))
                {
                    candidate.HasTested = true;
                }
                else //Not Tested
                {
                    candidate.HasTested = false;
                }

                // Calculate PERCENT MATCH JOB PROFILE SKILLS
                var job = user.JobApplications.First(x => x.Job.ProjectId == this.CurrentProjectId).Job;
                candidate.JobId = job.Id;
                var jobSkills = job.JobSkills;
                var userSkills = user.UsersSkills;
                foreach (var jobSkill in jobSkills)
                {
                    int userEsitmate = 0;
                    if (userSkills.Any(x => x.SkillId == jobSkill.SkillId))
                    {
                        userEsitmate = userSkills.Single(x => x.SkillId == jobSkill.SkillId).Estimate;
                    }

                    candidate.PercentMatchJobProfile += (userEsitmate - jobSkill.Estimate);
                }

                var totalJobSkillEst = jobSkills.Sum(x => x.Estimate);
                var candPercentage = candidate.PercentMatchJobProfile * 100 / totalJobSkillEst;
                candidate.PercentMatchJobProfile = (candPercentage<0) ? 100 -  Math.Abs(candPercentage) : candPercentage;

                // Calculate TESTS RESULTS
                var assignedTests = user.AssignedTests;
                var testCompleted = assignedTests.Count(assignedTest => user.TestResults.Any(x => x.TestId == assignedTest.Id));
                candidate.TestsCompleted = string.Format("{0} of {1}", testCompleted, assignedTests.Count);

                data.Add(candidate);
            }

            return Json(new { Data = data, TotalCount = totalCount }, JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetCurrentProjectJobsDropDownModel()
        {
            var model = this.jobsRepo.Where(x=>x.ProjectId == this.CurrentProjectId).Select(x => new KendoDropDownFKModel<long> { value = x.Id, text = x.Title });

            return Json(model, JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetJobProfileSkillsChartData(long? jobId, long? candidateId)
        {
            var skillsData = new List<ChartSeriesModel>();
            var skillNames = new List<string>();
            Job curJob = null;

            if (jobId.HasValue)
            {
                curJob = this.jobsRepo.FindOne(new ByIdSpecify<Job>(jobId.Value));

                skillNames.AddRange(curJob.JobSkills.Select(x => x.Skill).Select(x => x.Name));

                skillsData.Add(new ChartSeriesModel
                    {
                        Name = "Job Profile",
                        Data = curJob.JobSkills.Select(x => double.Parse(x.Estimate.ToString())).ToList()
                    });
            }

            if (candidateId.HasValue)
            {
                var candidate = this.usersRepo.FindOne(new ByIdSpecify<User>(candidateId.Value));

                var seriesName = string.Format("{0} {1} Profile", candidate.LastName, candidate.FirstName);

                var userSkills = new List<UserSkill>();
                if (curJob != null)
                {
                    userSkills = candidate.UsersSkills.Where(x => curJob.JobSkills.Any(a => a.SkillId == x.SkillId)).ToList();
                }
                else
                {
                    skillNames.AddRange(candidate.UsersSkills.Select(x => x.Skill).Select(x => x.Name));
                    userSkills = candidate.UsersSkills.ToList();
                }

                skillsData.Add(new ChartSeriesModel
                {
                    Name = seriesName,
                    Data = userSkills.Select(x => double.Parse(x.Estimate.ToString())).ToList()
                });
            }

            return this.Json(new { SkillNames = skillNames, SkillsData = skillsData }, JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetTestResultsForCandidate(long id)
        {
            var candidate = this.usersRepo.FindOne(new ByIdSpecify<User>(id));

            var assignedTests = candidate.AssignedTests;
            var passedTestsResults = candidate.TestResults.Where(x => assignedTests.Any(a => a.Id == x.TestId));

            var model = new List<TestResultModel>();
            
            foreach (var testRes in passedTestsResults)
            {
                var totalQuestions = testRes.Test.Questions.Count;
                var correctAnswers = testRes.ResultQuestions.Count(x => x.ResultAnswers.Any(a => a.IsCorrect && a.IsChoisen));
                var res = new TestResultModel
                    {
                        Name = testRes.Test.Name,
                        Category = testRes.Test.Category.Name,
                        PercentCorrectAnswers = (Math.Round((double)correctAnswers/totalQuestions*100, 2)).ToString(),
                        Result = string.Format("{0} of {1}", correctAnswers, totalQuestions)
                    };
                model.Add(res);
            }
            
            return Json(new {Data = model, TotalCount = model.Count}, JsonRequestBehavior.AllowGet);
        }
    }
}